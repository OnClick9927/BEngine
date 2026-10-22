using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class AssetBundlePackageSettings
{
    public static AssetBundlePackageSettingsDocument Load(ProjectWorkspace workspace) =>
        Load(workspace, EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    public static void Save(ProjectWorkspace workspace, AssetBundlePackageSettingsDocument settings) =>
        Save(workspace, settings, EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static AssetBundlePackageSettingsDocument Load(
        ProjectWorkspace workspace, FileUtility fileSystem, PathUtility paths)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var path = paths.Combine(workspace.ProjectSettingsPath, AssetBundlePackageSettingsDocument.FileName);
        var document = fileSystem.FileExists(path)
            ? YamlUtility.Deserialize<AssetBundlePackageSettingsDocument>(fileSystem.ReadAllText(path))
            : new AssetBundlePackageSettingsDocument();
        document.Validate();
        return document;
    }

    internal static void Save(ProjectWorkspace workspace, AssetBundlePackageSettingsDocument settings,
        FileUtility fileSystem, PathUtility paths)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        fileSystem.CreateDirectory(workspace.ProjectSettingsPath);
        var destination = paths.Combine(workspace.ProjectSettingsPath, AssetBundlePackageSettingsDocument.FileName);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            fileSystem.WriteAllText(temporary, YamlUtility.Serialize(settings));
            fileSystem.MoveFile(temporary, destination, true);
        }
        finally
        {
            if (fileSystem.FileExists(temporary)) fileSystem.DeleteFile(temporary);
        }
    }
}
