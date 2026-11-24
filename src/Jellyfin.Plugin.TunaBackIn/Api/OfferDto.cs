namespace Jellyfin.Plugin.TunaBackIn.Api;

/// <summary>
/// The prompt shown in jellyfin-web.
/// </summary>
/// <param name="Title">The "X" in Continue music streaming of "X".</param>
/// <param name="ItemCount">Number of tracks in the queue.</param>
/// <param name="CurrentIndex">Index of the track that will resume.</param>
public sealed record OfferDto(string Title, int ItemCount, int CurrentIndex);
