using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

internal static class TiledMapEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorIconRegistry.Register(typeof(Tilemap), EditorBuiltinIcons.Assets.Data);
        EditorIconRegistry.Register(typeof(TilemapRenderer), EditorBuiltinIcons.Assets.Image);
        AssetTypeRegistry.Register(".tilepalette.yaml", "Tile Palette", EditorBuiltinIcons.Assets.Image);
    }
}
