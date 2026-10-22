using System.Xml;
using BEngine.ProjectSystem;
using BEngine.Content;

namespace BEngine.Editor;

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
                !(asset.Replace('\\', '/').StartsWith("Assets/AOT/", StringComparison.OrdinalIgnoreCase) ||
                  asset.Replace('\\', '/').Equals("Assets/AOT", StringComparison.OrdinalIgnoreCase) ||
                  asset.Replace('\\', '/').StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase) ||
                  asset.Replace('\\', '/').StartsWith(PlayerPackagedResourceAddresses.CoreResourcesPrefix, StringComparison.OrdinalIgnoreCase)) ||
                asset.Replace('\\', '/').Split('/').Any(segment => segment is ".." or "."))
                throw new InvalidDataException($"Main-package preserved assets must be below Assets/AOT, Assets/Packages or core Assets/Resources: '{asset}'.");
        }
        result.PreservedAssets = result.PreservedAssets.Select(static p => p.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

public sealed class PlayerStrippingReport
{
    public string Format { get; set; } = "BEngine.PlayerStrippingReport";
    public int Version { get; set; } = 1;
    public string Target { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int DependencyPasses { get; set; }
    public List<PlayerStrippingReportEntry> Assemblies { get; set; } = [];
    public List<PlayerStrippingReportEntry> Resources { get; set; } = [];
    public string LinkerLog { get; set; } = string.Empty;
    public string HostPublishLogPath { get; set; } = string.Empty;
    public List<PlayerStrippingReportEntry> HostAssemblies { get; set; } = [];
    public bool TextBindingsIncluded { get; set; } = true;
    public List<string> LinkDescriptors { get; set; } = [];
    public string? ContentEncryptionKeyFingerprint { get; set; }
    public long AotArchiveBytes { get; set; }
    public long PlayerResourceArchiveBytes { get; set; }
    public List<string> GraphicsBackends { get; set; } = [];
    public bool RuntimeShaderCompilerIncluded { get; set; }
    public int OfflineShaderCount { get; set; }
    public long OfflineShaderBytes { get; set; }
    public string? HostContractPath { get; set; }
}

public sealed class PlayerStrippingReportEntry
{
    public string Name { get; set; } = string.Empty;
    public long OriginalBytes { get; set; }
    public long PublishedBytes { get; set; }
    public long? StoredBytes { get; set; }
    public bool Included { get; set; }
    public string Reason { get; set; } = string.Empty;
}
