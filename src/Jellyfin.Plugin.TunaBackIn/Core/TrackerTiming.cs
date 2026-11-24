using System;

namespace Jellyfin.Plugin.TunaBackIn.Core;

/// <summary>
/// Timing rules for <see cref="ResumeTracker"/>.
/// </summary>
/// <param name="MusicRecentWindow">How recently music must have played for a video start to arm a resume.</param>
/// <param name="SettleDelay">How long the device must be idle after the video before the offer shows
/// (so autoplaying the next episode doesn't flash the prompt).</param>
/// <param name="OfferExpiry">How long an offer stays valid.</param>
public sealed record TrackerTiming(TimeSpan MusicRecentWindow, TimeSpan SettleDelay, TimeSpan OfferExpiry)
{
    /// <summary>Gets the defaults.</summary>
    public static TrackerTiming Default { get; } = new(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(4), TimeSpan.FromHours(6));
}
