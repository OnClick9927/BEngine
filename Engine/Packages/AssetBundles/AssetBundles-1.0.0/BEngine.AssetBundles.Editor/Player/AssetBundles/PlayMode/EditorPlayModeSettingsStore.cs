using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using BEngine.Serialization;
using BEngine.AssetBundles;
using YamlDotNet.Core;

namespace BEngine.Editor.Build;

public static class EditorPlayModeSettingsStore
{
    public const string FileName = "EditorPlayMode.yaml";

    public static EditorPlayModeSettings Load(string projectPath) => Load(projectPath,
        EditorCapabilities.GetRequiredService<FileUtility>(), EditorCapabilities.GetRequiredService<PathUtility>());

    public static EditorPlayModeSettings Load(string projectPath, FileUtility files, PathUtility paths)
    {
        var workspace = ProjectWorkspace.Open(projectPath, files, paths);
        var path = paths.Combine(workspace.ProjectSettingsPath, FileName);
        var settings = files.FileExists(path)
            ? YamlUtility.Deserialize<EditorPlayModeSettings>(files.ReadAllText(path), strict: true)
            : CreateDefault(workspace, files, paths);
        Validate(settings);
        return settings;
    }

    public static void Save(string projectPath, EditorPlayModeSettings settings) => Save(projectPath, settings,
        EditorCapabilities.GetRequiredService<FileUtility>(), EditorCapabilities.GetRequiredService<PathUtility>());

    public static void Save(string projectPath, EditorPlayModeSettings settings, FileUtility files, PathUtility paths)
    {
        Validate(settings);
        var workspace = ProjectWorkspace.Open(projectPath, files, paths);
        files.CreateDirectory(workspace.ProjectSettingsPath);
        files.WriteAllText(paths.Combine(workspace.ProjectSettingsPath, FileName), YamlUtility.Serialize(settings));
    }

    private static EditorPlayModeSettings CreateDefault(ProjectWorkspace workspace, FileUtility files, PathUtility paths)
    {
        var settings = new EditorPlayModeSettings();
        var bundleSettings = paths.Combine(workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        if (!files.FileExists(bundleSettings)) return settings;
        try
        {
            var release = YamlUtility.Deserialize<AssetBundleSettingsDocument>(files.ReadAllText(bundleSettings));
            if (!string.IsNullOrWhiteSpace(release.PackageName))
            {
                RuntimeAssetReferences.ValidateOptionalRelativePath(release.PackageName, nameof(release.PackageName));
                settings.PackageName = release.PackageName;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or YamlException or ArgumentException)
        {
            // An unrelated broken Player build configuration must not disable the default virtual session.
            // Explicit Editor Play Mode files are validated above and are never silently replaced.
        }
        return settings;
    }

    private static void Validate(EditorPlayModeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Mode)) throw new InvalidDataException("Unknown Editor Play Mode content selection.");
        if (settings.ReleaseDirectory is null || string.IsNullOrWhiteSpace(settings.PackageName))
            throw new InvalidDataException("Editor Play Mode requires a release identity and a valid directory value.");
        RuntimeAssetReferences.ValidateOptionalRelativePath(settings.PackageName, nameof(settings.PackageName));
    }
}
