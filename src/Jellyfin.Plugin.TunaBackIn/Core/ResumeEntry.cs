using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.TunaBackIn.Core;

/// <summary>
/// Where a device is in the snapshot → pending → offer cycle.
/// </summary>
public enum ResumeState
{
    /// <summary>Music is playing or nothing to offer.</summary>
    Idle,

    /// <summary>A non-audio item started right after music; waiting for it to end.</summary>
    Pending,

    /// <summary>The non-audio item ended; the music can be resumed.</summary>
    Offered
}

/// <summary>
/// The latest music queue seen for one user on one device, plus its resume state.
/// </summary>
public sealed class ResumeEntry
{
    /// <summary>Gets or sets the user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the device id.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the queue's item ids, in play order.</summary>
    public IReadOnlyList<Guid> ItemIds { get; set; } = [];

    /// <summary>Gets or sets the queue's playlist item ids (parallel to <see cref="ItemIds"/>, may contain nulls).</summary>
    public IReadOnlyList<string?> PlaylistItemIds { get; set; } = [];

    /// <summary>Gets or sets the index of the current track in <see cref="ItemIds"/>.</summary>
    public int CurrentIndex { get; set; }

    /// <summary>Gets or sets the position in the current track.</summary>
    public long PositionTicks { get; set; }

    /// <summary>Gets or sets the display title shown in the prompt (filled in when offered).</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the resume state.</summary>
    public ResumeState State { get; set; }

    /// <summary>Gets or sets the last time audio progress or stop was seen.</summary>
    public DateTimeOffset LastAudioActivity { get; set; }

    /// <summary>Gets or sets the last time a non-audio item started on this device.</summary>
    public DateTimeOffset? LastNonAudioStart { get; set; }

    /// <summary>Gets or sets when the entry became <see cref="ResumeState.Offered"/>.</summary>
    public DateTimeOffset? OfferedAt { get; set; }

    /// <summary>Gets the store key.</summary>
    public string Key => ResumeTracker.KeyFor(UserId, DeviceId);
}
