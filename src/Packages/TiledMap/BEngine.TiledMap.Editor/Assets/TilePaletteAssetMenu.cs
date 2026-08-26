using System.Text;
using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

internal static class TilePaletteAssetMenu
{
    [MenuItem("Assets/Create/2D/Tile Palette", false, 250)]
    private static void CreatePalette()
    {
        var folder = ProjectWindowUtil.GetActiveFolderPath();
        if (folder is null) return;
        var assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{folder.TrimEnd('/', '\\')}/New Tile Palette.tilepalette.yaml");
        var fullPath = Path.GetFullPath(Path.Combine(EditorApplication.projectPath,
            assetPath.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var palette = new TilePalette();
        palette.SliceAtlas(1, 1);
        File.WriteAllText(fullPath, BEngine.Serialization.YamlUtility.Serialize(palette),
            new UTF8Encoding(false));
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ProjectWindowUtil.ShowCreatedAsset(assetPath, beginRename: true);
    }

    [MenuItem("Assets/Create/2D/Tile Palette", true)]
    private static bool ValidateCreatePalette() => ProjectWindowUtil.GetActiveFolderPath() is not null;
}
