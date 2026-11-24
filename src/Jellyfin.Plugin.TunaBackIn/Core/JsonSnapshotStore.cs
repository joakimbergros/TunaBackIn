using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TunaBackIn.Core;

/// <summary>
/// Stores entries as one JSON file, written atomically.
/// </summary>
public sealed class JsonSnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSnapshotStore"/> class.
    /// </summary>
    /// <param name="path">The JSON file path.</param>
    public JsonSnapshotStore(string path)
    {
        _path = path;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ResumeEntry> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<List<ResumeEntry>>(stream, _options) ?? [];
        }
        catch (JsonException)
        {
            // A corrupt file only loses resume offers; start clean rather than failing the plugin.
            return [];
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyCollection<ResumeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries, _options));
        File.Move(tmp, _path, overwrite: true);
    }
}
