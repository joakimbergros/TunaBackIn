using System;

namespace Jellyfin.Plugin.TunaBackIn.Core;

/// <summary>
/// One entry of a session's play queue.
/// </summary>
/// <param name="ItemId">The library item id.</param>
/// <param name="PlaylistItemId">The client's id for this queue slot (distinguishes repeated tracks).</param>
public readonly record struct QueueItemRef(Guid ItemId, string? PlaylistItemId);
