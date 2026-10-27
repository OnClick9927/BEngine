using System.Text.Json;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class WebPlayerContentLayout
{
    private const string AssemblyDirectoryMarker = "assembly/bengine.directory";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "index.html", "player.bin", "framework.bin", "main.js", "framework.js", "text.js", "video.js",
        "bengine-content.json", "bengine-content", "_framework", "brotli-decoder.wasm", "BrotliDecoder.LICENSE.txt"
    };

    internal static void Apply(string stagingRoot, string webRoot, FileUtility files, PathUtility paths,
        bool httpStreamingAssets = false)
    {
        var dataDirectories = files.EnumerateDirectories(stagingRoot, "*_data", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal).ToArray();
        if (dataDirectories.Length != 1)
            throw new InvalidDataException($"The Web Player requires exactly one staged '*_data' directory; found {dataDirectories.Length}.");
        var dataDirectory = dataDirectories[0];
        var dataDirectoryName = paths.GetFileName(dataDirectory);
        var entries = new List<WebContentEntry>();
        var sources = files.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(source => new
            {
                Source = source,
                Relative = paths.GetRelativePath(dataDirectory, source).Replace('\\', '/')
            })
            // Web does not need an assembly directory when all code is packed.
            // Keep real external files, but omit the native Player's placeholder.
            .Where(source => !source.Relative.Equals(AssemblyDirectoryMarker, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        // Check the entire content tree before overwriting any SDK-generated file.
        foreach (var source in sources)
        {
            var segments = source.Relative.Split('/');
            var rootName = segments[0];
            if (rootName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || rootName.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
                rootName = rootName[..^3];
            if (segments.Any(segment => segment is "" or "." or "..") || ReservedNames.Contains(rootName))
                throw new InvalidDataException($"Web Player content has a reserved or invalid root path: '{source.Relative}'.");
        }
        foreach (var source in sources)
        {
            var destination = paths.Combine(webRoot, source.Relative.Replace('/', paths.DirectorySeparatorChar));
            files.CreateDirectory(paths.GetDirectoryName(destination)!);
            files.CopyFile(source.Source, destination, overwrite: true);
            if (httpStreamingAssets && source.Relative.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase))
                continue;
            entries.Add(new("./" + string.Join('/', source.Relative.Split('/').Select(Uri.EscapeDataString)),
                $"{dataDirectoryName}/{source.Relative}"));
        }
        if (entries.Count == 0) throw new InvalidDataException("The Web Player has no packaged content to mount.");
        files.WriteAllText(paths.Combine(webRoot, "bengine-content.json"), JsonSerializer.Serialize(
            new { dataDirectory = dataDirectoryName, streamingAssetsTransport = httpStreamingAssets ? "http" : "vfs", files = entries },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private sealed record WebContentEntry(string Url, string VirtualPath);
}
