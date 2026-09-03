using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;
using YamlDotNet.RepresentationModel;

namespace BEngine.Editor;

internal static partial class PlayerAssetDependencyCollector
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".yaml", ".yml", ".json", ".xml", ".uxml", ".uss", ".txt", ".md",
        ".shader", ".cg", ".html", ".htm"
    };

    private static readonly HashSet<string> ReferencedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".yaml", ".yml", ".json", ".xml", ".uxml", ".uss", ".txt", ".png",
        ".shader", ".cg", ".html", ".htm", ".wav", ".mp3", ".ogg", ".ttf", ".otf",
        ".woff", ".woff2", ".bytes", ".bin", ".csv"
    };

    internal static IReadOnlyList<string> Collect(
        ProjectWorkspace workspace,
        ProjectAssetDatabase database,
        IReadOnlyList<string> scenePaths,
        bool includeImplicitRuntimeRoots = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(scenePaths);
        if (scenePaths.Count == 0)
            throw new InvalidDataException("At least one enabled scene is required for a Player build.");

        var mainAssets = database.assets
            .Where(static asset => !asset.IsDirectory && !asset.IsSubAsset && IsRuntimeAsset(asset.AssetPath))
            .ToDictionary(static asset => NormalizePath(asset.AssetPath), StringComparer.OrdinalIgnoreCase);
        var pathsByGuid = new Dictionary<Guid, string>();
        foreach (var asset in database.assets.Where(static asset => !asset.IsDirectory))
        {
            var ownerPath = NormalizePath(asset.AssetPath);
            pathsByGuid.TryAdd(asset.Guid, ownerPath);
            if (asset.ParentGuid is { } parentGuid) pathsByGuid.TryAdd(parentGuid, ownerPath);
        }

        var pending = new Queue<string>();
        foreach (var scenePath in scenePaths)
        {
            var normalized = NormalizePath(scenePath);
            if (!normalized.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Scenes In Build entry is not a scene: '{scenePath}'.");
            if (!mainAssets.ContainsKey(normalized))
                throw new FileNotFoundException($"Scenes In Build entry was not found: '{normalized}'.",
                    workspace.ResolveInside(normalized));
            pending.Enqueue(normalized);
        }

        if (includeImplicitRuntimeRoots)
        {
            // Resources and StreamingAssets are addressable without a serialized reference.
            foreach (var path in mainAssets.Keys.Where(IsImplicitRuntimeRoot).OrderBy(static path => path,
                         StringComparer.OrdinalIgnoreCase))
                pending.Enqueue(path);
        }

        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var assetPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!included.Add(assetPath) || !mainAssets.TryGetValue(assetPath, out var asset)) continue;
            foreach (var reference in EnumerateReferences(asset, cancellationToken))
            {
                if (TryResolveReference(assetPath, reference, pathsByGuid, mainAssets, out var dependency) &&
                    !included.Contains(dependency))
                    pending.Enqueue(dependency);
            }
        }

        return included.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> CollectAll(
        ProjectAssetDatabase database,
        Func<string, bool>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        var result = new List<string>();
        foreach (var asset in database.assets
                     .Where(static asset => !asset.IsDirectory && !asset.IsSubAsset &&
                                            IsRuntimeAsset(asset.AssetPath))
                     .OrderBy(static asset => asset.AssetPath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizePath(asset.AssetPath);
            if (predicate?.Invoke(path) != false) result.Add(path);
        }
        return result;
    }

    private static IEnumerable<string> EnumerateReferences(
        ProjectAssetRecord asset,
        CancellationToken cancellationToken)
    {
        var paths = new[] { asset.SourcePath, asset.ArtifactPath }
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TextExtensions.Contains(Path.GetExtension(path))) continue;
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }

            foreach (Match match in GuidReferencePattern().Matches(text)) yield return match.Value;
            foreach (Match match in AssetPathPattern().Matches(text)) yield return match.Value;
            foreach (var scalar in EnumerateStructuredStrings(path, text)) yield return scalar;
        }
    }

    private static IEnumerable<string> EnumerateStructuredStrings(string path, string text)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            YamlStream yaml;
            try
            {
                yaml = new YamlStream();
                using var reader = new StringReader(text);
                yaml.Load(reader);
            }
            catch (Exception) { yield break; }
            foreach (var document in yaml.Documents)
            foreach (var value in EnumerateYamlScalars(document.RootNode))
                yield return value;
            yield break;
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(text); }
            catch (JsonException) { yield break; }
            using (document)
                foreach (var value in EnumerateJsonStrings(document.RootElement)) yield return value;
            yield break;
        }

        if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".uxml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)) yield break;
        XDocument xml;
        try { xml = XDocument.Parse(text, LoadOptions.None); }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
        {
            yield break;
        }
        foreach (var element in xml.Descendants())
        {
            if (!string.IsNullOrWhiteSpace(element.Value)) yield return element.Value;
            foreach (var attribute in element.Attributes()) yield return attribute.Value;
        }
    }

    private static IEnumerable<string> EnumerateYamlScalars(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar when !string.IsNullOrWhiteSpace(scalar.Value):
                yield return scalar.Value;
                break;
            case YamlSequenceNode sequence:
                foreach (var child in sequence.Children)
                foreach (var value in EnumerateYamlScalars(child))
                    yield return value;
                break;
            case YamlMappingNode mapping:
                foreach (var pair in mapping.Children)
                {
                    foreach (var value in EnumerateYamlScalars(pair.Key)) yield return value;
                    foreach (var value in EnumerateYamlScalars(pair.Value)) yield return value;
                }
                break;
        }
    }

    private static IEnumerable<string> EnumerateJsonStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (element.GetString() is { Length: > 0 } value) yield return value;
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                foreach (var childValue in EnumerateJsonStrings(child))
                    yield return childValue;
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                foreach (var propertyValue in EnumerateJsonStrings(property.Value))
                    yield return propertyValue;
                break;
        }
    }

    private static bool TryResolveReference(
        string ownerPath,
        string reference,
        IReadOnlyDictionary<Guid, string> pathsByGuid,
        IReadOnlyDictionary<string, ProjectAssetRecord> assets,
        out string assetPath)
    {
        assetPath = string.Empty;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var guidMatch = GuidReferencePattern().Match(reference);
        if (guidMatch.Success && Guid.TryParse(guidMatch.Groups[1].Value, out var guid) &&
            pathsByGuid.TryGetValue(guid, out var guidPath) && assets.ContainsKey(guidPath))
        {
            assetPath = guidPath;
            return true;
        }

        var trimmed = reference.Trim().Trim('"', '\'', '(', ')');
        var fragment = trimmed.IndexOf('#');
        if (fragment >= 0) trimmed = trimmed[..fragment];
        var query = trimmed.IndexOf('?');
        if (query >= 0) trimmed = trimmed[..query];
        if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
        {
            var direct = NormalizePath(trimmed.TrimEnd(')', ']', '}', ',', ';'));
            if (assets.ContainsKey(direct))
            {
                assetPath = direct;
                return true;
            }
        }

        foreach (Match match in AssetPathPattern().Matches(reference))
        {
            var candidate = NormalizePath(match.Value.TrimEnd(')', ']', '}', ',', ';'));
            if (!assets.ContainsKey(candidate)) continue;
            assetPath = candidate;
            return true;
        }

        if (trimmed.Length == 0 || trimmed.Contains("//", StringComparison.Ordinal) ||
            !ReferencedAssetExtensions.Contains(Path.GetExtension(trimmed))) return false;
        var relative = NormalizePath(Path.Combine(Path.GetDirectoryName(ownerPath) ?? "Assets", trimmed));
        if (!assets.ContainsKey(relative)) return false;
        assetPath = relative;
        return true;
    }

    private static bool IsImplicitRuntimeRoot(string path)
    {
        var segments = NormalizePath(path).Split('/');
        return segments.Any(static segment =>
            segment.Equals("Resources", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRuntimeAsset(string path)
    {
        var normalized = NormalizePath(path);
        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase)) return false;
        return !normalized.Split('/').Skip(1).Any(static segment =>
            segment.Equals("Editor", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    [GeneratedRegex(@"guid:\s*([0-9a-fA-F]{32}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GuidReferencePattern();

    [GeneratedRegex(@"Assets[/\\][A-Za-z0-9_ .\-/\\]+?\.(?:scene\.yaml|prefab\.yaml|asset\.yaml|atlas\.yaml|material\.yaml|yaml|yml|json|xml|uxml|uss|txt|png|shader|cg|html|htm|wav|mp3|ogg|ttf|otf|woff|woff2|bytes|bin|csv)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AssetPathPattern();
}
