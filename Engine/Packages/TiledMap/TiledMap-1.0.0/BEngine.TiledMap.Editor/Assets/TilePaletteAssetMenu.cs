using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.TiledMap.Editor;

internal static class TilePaletteAssetMenu
{
    [MenuItem("Assets/Create/2D/Tile Palette", false, 250)]
    private static void CreatePalette()
    {
        var folder = ProjectWindowUtil.GetActiveFolderPath();
        if (folder is null) return;
        var fileSystem = EditorServices.GetRequiredService<FileUtility>();
        var paths = EditorServices.GetRequiredService<PathUtility>();
        var assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{folder.TrimEnd('/', '\\')}/New Tile Palette.tilepalette.yaml");
        var fullPath = paths.GetFullPath(paths.Combine(EditorApplication.projectPath,
            assetPath.Replace('/', paths.DirectorySeparatorChar)));
        fileSystem.CreateDirectory(paths.GetDirectoryName(fullPath)!);
        var palette = new TilePalette();
        palette.SliceAtlas(1, 1);
        fileSystem.WriteAllText(fullPath, BEngine.YamlUtility.Serialize(palette));
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ProjectWindowUtil.ShowCreatedAsset(assetPath, beginRename: true);
    }

    [MenuItem("Assets/Create/2D/Tile Palette", true)]
    private static bool ValidateCreatePalette() => ProjectWindowUtil.GetActiveFolderPath() is not null;
}
