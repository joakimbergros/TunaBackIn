using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.TunaBackIn.Core;

/// <summary>
/// Thread-safe state machine: tracks the latest music queue per user+device and decides when to offer it back.
/// Free of Jellyfin types so it can be unit-tested.
/// </summary>
public sealed class ResumeTracker
{
    /// <summary>
    /// Audio events that arrive this soon after a non-audio start are treated as the old music
    /// stopping (event order between sessions is not guaranteed), not as music playing again.
    /// </summary>
    internal static readonly TimeSpan ReorderTolerance = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private readonly Dictionary<string, ResumeEntry> _entries;
    private readonly ISnapshotStore _store;
    private readonly Func<TrackerTiming> _timing;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeTracker"/> class.
    /// </summary>
    /// <param name="store">The persistence store.</param>
    /// <param name="timing">Supplies the current timing rules (read on every call so config changes apply live).</param>
    public ResumeTracker(ISnapshotStore store, Func<TrackerTiming> timing)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _timing = timing;
        _entries = store.Load().ToDictionary(e => e.Key, StringComparer.Ordinal);
    }

    /// <summary>Builds the key for a user and device.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(Guid userId, string deviceId) => userId.ToString("N") + ":" + deviceId;

    /// <summary>
    /// Records music playing (start or progress).
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="queue">The session's queue; null, empty, or not containing <paramref name="currentItemId"/> keeps the last known queue.</param>
    /// <param name="currentItemId">The item playing now.</param>
    /// <param name="currentPlaylistItemId">The queue slot playing now, if known.</param>
    /// <param name="positionTicks">Position in the current item.</param>
    /// <param name="now">Current time.</param>
    public void AudioActivity(
        Guid userId,
        string deviceId,
        IReadOnlyList<QueueItemRef>? queue,
        Guid currentItemId,
        string? currentPlaylistItemId,
        long positionTicks,
        DateTimeOffset now)
    {
        lock (_lock)
        {
            var key = KeyFor(userId, deviceId);
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new ResumeEntry { UserId = userId, DeviceId = deviceId };
                _entries[key] = entry;
            }

            if (IsJustAfterNonAudioStart(entry, now))
            {
                // A late report from the music that the video replaced: refresh the position, keep the state.
                UpdatePosition(entry, currentItemId, currentPlaylistItemId, positionTicks);
                entry.LastAudioActivity = now;
                return;
            }

            var wasOffered = entry.State != ResumeState.Idle;
            // Only trust a queue that contains the track playing now: right after a video the session can
            // still carry the video's queue when the music's start event fires.
            if (queue is { Count: > 0 } && queue.Any(q => q.ItemId == currentItemId))
            {
                entry.ItemIds = queue.Select(q => q.ItemId).ToArray();
                entry.PlaylistItemIds = queue.Select(q => q.PlaylistItemId).ToArray();
            }

            UpdatePosition(entry, currentItemId, currentPlaylistItemId, positionTicks);
            entry.LastAudioActivity = now;
            entry.State = ResumeState.Idle;
            entry.OfferedAt = null;
            entry.Title = null;

            if (wasOffered)
            {
                Persist();
            }
        }
    }

    /// <summary>
    /// Records music stopping. Persists the snapshot.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="itemId">The item that stopped.</param>
    /// <param name="positionTicks">Its final position, if reported.</param>
    /// <param name="now">Current time.</param>
    public void AudioStopped(Guid userId, string deviceId, Guid itemId, long? positionTicks, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(KeyFor(userId, deviceId), out var entry) || entry.ItemIds.Count == 0)
            {
                return;
            }

            if (positionTicks is { } pos && CurrentItemId(entry) == itemId)
            {
                entry.PositionTicks = pos;
            }

            entry.LastAudioActivity = now;

            // Video start was processed before this stop: arm now.
            if (entry.State == ResumeState.Idle && IsJustAfterNonAudioStart(entry, now))
            {
                entry.State = ResumeState.Pending;
            }

            Persist();
        }
    }

    /// <summary>
    /// Records a non-audio (video) item starting. Arms the resume if music was just playing.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="now">Current time.</param>
    public void NonAudioStarted(Guid userId, string deviceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(KeyFor(userId, deviceId), out var entry) || entry.ItemIds.Count == 0)
            {
                return;
            }

            entry.LastNonAudioStart = now;
            var musicWasRecent = now - entry.LastAudioActivity <= _timing().MusicRecentWindow;

            if (entry.State == ResumeState.Offered || (entry.State == ResumeState.Idle && musicWasRecent))
            {
                // Offered → Pending covers autoplay of the next episode.
                entry.State = ResumeState.Pending;
                entry.OfferedAt = null;
                Persist();
            }
        }
    }

    /// <summary>
    /// Records a non-audio item stopping. Turns a pending resume into an offer.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The entry if it is now offered, else null.</returns>
    public ResumeEntry? NonAudioStopped(Guid userId, string deviceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(KeyFor(userId, deviceId), out var entry) || entry.State != ResumeState.Pending)
            {
                return null;
            }

            entry.State = ResumeState.Offered;
            entry.OfferedAt = now;
            Persist();
            return Clone(entry);
        }
    }

    /// <summary>
    /// Sets the display title of an entry.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="title">The title.</param>
    public void SetTitle(Guid userId, string deviceId, string title)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(KeyFor(userId, deviceId), out var entry))
            {
                entry.Title = title;
                Persist();
            }
        }
    }

    /// <summary>
    /// Gets the offer for a device if it should be shown now.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="deviceIsPlaying">Whether the device is currently playing anything.</param>
    /// <param name="now">Current time.</param>
    /// <returns>A copy of the entry, or null.</returns>
    public ResumeEntry? GetOffer(Guid userId, string deviceId, bool deviceIsPlaying, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (deviceIsPlaying
                || !_entries.TryGetValue(KeyFor(userId, deviceId), out var entry)
                || entry.State != ResumeState.Offered
                || entry.OfferedAt is not { } offeredAt)
            {
                return null;
            }

            var timing = _timing();
            var age = now - offeredAt;
            if (age > timing.OfferExpiry)
            {
                entry.State = ResumeState.Idle;
                entry.OfferedAt = null;
                Persist();
                return null;
            }

            return age >= timing.SettleDelay ? Clone(entry) : null;
        }
    }

    /// <summary>Gets a copy of the entry for inspection, regardless of state.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <returns>The entry or null.</returns>
    public ResumeEntry? Get(Guid userId, string deviceId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(KeyFor(userId, deviceId), out var entry) ? Clone(entry) : null;
        }
    }

    /// <summary>Finds entries for a device (any user).</summary>
    /// <param name="deviceId">The device id.</param>
    /// <returns>Copies of the matching entries.</returns>
    public IReadOnlyList<ResumeEntry> FindByDevice(string deviceId)
    {
        lock (_lock)
        {
            return _entries.Values.Where(e => e.DeviceId == deviceId).Select(Clone).ToList();
        }
    }

    /// <summary>Drops a pending or offered resume without playing it.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <returns>True if something was dismissed.</returns>
    public bool Dismiss(Guid userId, string deviceId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(KeyFor(userId, deviceId), out var entry) || entry.State == ResumeState.Idle)
            {
                return false;
            }

            entry.State = ResumeState.Idle;
            entry.OfferedAt = null;
            Persist();
            return true;
        }
    }

    private static Guid? CurrentItemId(ResumeEntry entry) =>
        entry.CurrentIndex >= 0 && entry.CurrentIndex < entry.ItemIds.Count ? entry.ItemIds[entry.CurrentIndex] : null;

    private static void UpdatePosition(ResumeEntry entry, Guid currentItemId, string? currentPlaylistItemId, long positionTicks)
    {
        var index = -1;
        if (!string.IsNullOrEmpty(currentPlaylistItemId))
        {
            index = IndexOf(entry.PlaylistItemIds, currentPlaylistItemId);
        }

        if (index < 0)
        {
            // Prefer the slot at or after the current one, so a track repeated earlier in the queue doesn't win.
            index = FindItemFrom(entry.ItemIds, currentItemId, Math.Max(entry.CurrentIndex, 0));
        }

        if (index >= 0)
        {
            entry.CurrentIndex = index;
            entry.PositionTicks = positionTicks;
        }
    }

    private static int IndexOf(IReadOnlyList<string?> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindItemFrom(IReadOnlyList<Guid> ids, Guid id, int start)
    {
        for (var n = 0; n < ids.Count; n++)
        {
            var i = (start + n) % ids.Count;
            if (ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsJustAfterNonAudioStart(ResumeEntry entry, DateTimeOffset now) =>
        entry.LastNonAudioStart is { } started && now - started >= TimeSpan.Zero && now - started <= ReorderTolerance;

    private static ResumeEntry Clone(ResumeEntry e) => new()
    {
        UserId = e.UserId,
        DeviceId = e.DeviceId,
        ItemIds = e.ItemIds.ToArray(),
        PlaylistItemIds = e.PlaylistItemIds.ToArray(),
        CurrentIndex = e.CurrentIndex,
        PositionTicks = e.PositionTicks,
        Title = e.Title,
        State = e.State,
        LastAudioActivity = e.LastAudioActivity,
        LastNonAudioStart = e.LastNonAudioStart,
        OfferedAt = e.OfferedAt
    };

    private void Persist() => _store.Save(_entries.Values.ToList());
}
