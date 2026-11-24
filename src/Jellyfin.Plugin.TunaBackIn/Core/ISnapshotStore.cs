using System.Collections.Generic;

namespace Jellyfin.Plugin.TunaBackIn.Core;

/// <summary>
/// Persists resume entries across server restarts.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>Loads all entries.</summary>
    /// <returns>The stored entries.</returns>
    IReadOnlyCollection<ResumeEntry> Load();

    /// <summary>Replaces the stored entries.</summary>
    /// <param name="entries">The entries to store.</param>
    void Save(IReadOnlyCollection<ResumeEntry> entries);
}
