using BEngine.ProjectSystem;


namespace BEngine.Editor.Build;

/// <summary>Explicit roots for content reached through runtime strings or reflection.</summary>
public sealed class PlayerStrippingSettings
{
    public const string FileName = "PlayerStripping.yaml";
    public string Format { get; set; } = "BEngine.PlayerStripping";
    public int Version { get; set; } = 1;
    public List<string> PreservedAssets { get; set; } = [];
    public List<string> PreservedTypes { get; set; } = [];

    internal static PlayerStrippingSettings Load(ProjectWorkspace workspace)
    {
        var files = EditorHostServices.FileSystem;
        var path = EditorHostServices.Paths.Combine(workspace.ProjectSettingsPath, FileName);
        var result = files.FileExists(path)
            ? YamlUtility.Deserialize<PlayerStrippingSettings>(files.ReadAllText(path))
            : new PlayerStrippingSettings();
        if (result.Format != "BEngine.PlayerStripping" || result.Version != 1 ||
            result.PreservedAssets is null || result.PreservedTypes is null)
            throw new InvalidDataException($"Invalid stripping settings: '{path}'.");
        foreach (var asset in result.PreservedAssets)
        {
            if (string.IsNullOrWhiteSpace(asset) ||
                !asset.Replace('\\', '/').StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || asset.Length <= "Assets/".Length ||
                asset.AsSpan().HasPathTraversal())
                throw new InvalidDataException($"Preserved asset addresses must remain below Assets: '{asset}'.");
        }
        result.PreservedAssets = ((((global::System.Collections.Generic.IEnumerable<string>)(result.PreservedAssets.Select(static p => p.Replace('\\', '/')))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => new global::System.Collections.Generic.List<string>(),
            var (distinctItems2082, distinctComparer2082) => distinctItems2082.ToHashSet<string>(distinctComparer2082).ToList()});
        if (result.PreservedTypes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Preserved type names cannot be empty.");
        return result;
    }

    internal static IReadOnlyList<string> GetLinkDescriptors(ProjectWorkspace workspace)
    {
        var files = EditorHostServices.FileSystem;
        var project = EditorHostServices.Paths.Combine(workspace.ProjectSettingsPath, "link.xml");
        var assets = EditorHostServices.Paths.Combine(workspace.RootPath, "Assets");
        var paths = files.DirectoryExists(assets)
            ? files.EnumerateFiles(assets, "link.xml", SearchOption.AllDirectories)
            : [];
        return paths.Concat(files.FileExists(project) ? [project] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static p => p, StringComparer.Ordinal).ToArray();
    }
}


