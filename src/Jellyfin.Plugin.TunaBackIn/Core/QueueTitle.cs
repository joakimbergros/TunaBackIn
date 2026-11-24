using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.TunaBackIn.Core;

/// <summary>
/// Builds the "X" in <c>Continue music streaming of "X"</c>.
/// </summary>
public static class QueueTitle
{
    /// <summary>
    /// Builds the title from the queue's album names.
    /// </summary>
    /// <param name="albums">Album name per queue item (null when unknown).</param>
    /// <param name="currentIndex">Index of the current track.</param>
    /// <param name="currentTrackName">Name of the current track, used when no album is known.</param>
    /// <returns>The album name if the whole queue is one album, else "Album +N more", else the track name.</returns>
    public static string Build(IReadOnlyList<string?> albums, int currentIndex, string? currentTrackName)
    {
        ArgumentNullException.ThrowIfNull(albums);
        var distinct = albums
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var current = currentIndex >= 0 && currentIndex < albums.Count ? albums[currentIndex] : null;
        if (string.IsNullOrWhiteSpace(current))
        {
            current = distinct.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return string.IsNullOrWhiteSpace(currentTrackName) ? "your music" : currentTrackName;
        }

        var others = distinct.Count - 1;
        return others <= 0
            ? current
            : string.Format(CultureInfo.InvariantCulture, "{0} +{1} more", current, others);
    }
}
