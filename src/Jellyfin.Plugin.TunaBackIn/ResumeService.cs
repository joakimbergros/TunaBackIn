using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TunaBackIn.Core;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TunaBackIn;

/// <summary>
/// Result of a resume attempt.
/// </summary>
public enum ResumeResult
{
    /// <summary>The play command was sent.</summary>
    Sent,

    /// <summary>No snapshot exists for the device.</summary>
    NothingToResume,

    /// <summary>The device has no active session that accepts remote control.</summary>
    DeviceUnavailable
}

/// <summary>
/// Talks to Jellyfin on behalf of the tracker: finds sessions, builds titles, sends play commands.
/// </summary>
public sealed class ResumeService
{
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ResumeTracker _tracker;
    private readonly ILogger<ResumeService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeService"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tracker">The tracker.</param>
    /// <param name="logger">The logger.</param>
    public ResumeService(ISessionManager sessionManager, ILibraryManager libraryManager, ResumeTracker tracker, ILogger<ResumeService> logger)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _tracker = tracker;
        _logger = logger;
    }

    /// <summary>Finds the active session of a user on a device.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <returns>The session or null.</returns>
    public SessionInfo? FindSession(Guid userId, string deviceId) =>
        _sessionManager.Sessions
            .Where(s => s.IsActive && s.UserId.Equals(userId) && string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal))
            .OrderByDescending(s => s.LastActivityDate)
            .FirstOrDefault();

    /// <summary>Whether the device is currently playing anything.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <returns>True if playing.</returns>
    public bool IsPlaying(Guid userId, string deviceId) => FindSession(userId, deviceId)?.NowPlayingItem is not null;

    /// <summary>Builds and stores the prompt title for an entry.</summary>
    /// <param name="entry">The entry.</param>
    public void FillTitle(ResumeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var items = entry.ItemIds.Select(id => _libraryManager.GetItemById(id)).ToList();
        var albums = items.Select(i => (i as Audio)?.Album).ToList();
        var current = entry.CurrentIndex >= 0 && entry.CurrentIndex < items.Count ? items[entry.CurrentIndex]?.Name : null;
        var title = QueueTitle.Build(albums, entry.CurrentIndex, current);
        _tracker.SetTitle(entry.UserId, entry.DeviceId, title);
        entry.Title = title;
    }

    /// <summary>Plays the saved queue on the device it came from.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ResumeResult> ResumeAsync(Guid userId, string deviceId, CancellationToken cancellationToken)
    {
        var entry = _tracker.Get(userId, deviceId);
        if (entry is null || entry.ItemIds.Count == 0)
        {
            return ResumeResult.NothingToResume;
        }

        var session = FindSession(userId, deviceId);
        if (session is null || !session.SupportsRemoteControl)
        {
            return ResumeResult.DeviceUnavailable;
        }

        var request = new PlayRequest
        {
            ItemIds = entry.ItemIds.ToArray(),
            StartIndex = entry.CurrentIndex,
            StartPositionTicks = entry.PositionTicks,
            PlayCommand = PlayCommand.PlayNow,
            ControllingUserId = userId
        };

        _logger.LogInformation(
            "Resuming {Count} items at index {Index} on {Device}",
            request.ItemIds.Length,
            entry.CurrentIndex,
            session.DeviceName);

        await _sessionManager.SendPlayCommand(session.Id, session.Id, request, cancellationToken).ConfigureAwait(false);
        _tracker.Dismiss(userId, deviceId);
        return ResumeResult.Sent;
    }
}
