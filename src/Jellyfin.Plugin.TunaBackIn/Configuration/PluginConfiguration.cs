using System;
using Jellyfin.Plugin.TunaBackIn.Core;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TunaBackIn.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether jellyfin-web shows the "Continue music" prompt.
    /// </summary>
    public bool ShowPrompt { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether music resumes automatically when the video ends, without prompting.
    /// </summary>
    public bool AutoResume { get; set; }

    /// <summary>
    /// Gets or sets how many seconds before a video start music must have been playing to arm a resume.
    /// </summary>
    public int MusicRecentSeconds { get; set; } = 120;

    /// <summary>
    /// Gets or sets how many hours an offer stays valid.
    /// </summary>
    public int OfferExpiryHours { get; set; } = 6;

    /// <summary>
    /// Builds the tracker timing from this configuration.
    /// </summary>
    /// <returns>The timing.</returns>
    public TrackerTiming ToTiming() => TrackerTiming.Default with
    {
        MusicRecentWindow = TimeSpan.FromSeconds(Math.Max(5, MusicRecentSeconds)),
        OfferExpiry = TimeSpan.FromHours(Math.Max(1, OfferExpiryHours))
    };
}
