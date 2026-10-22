using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

[CustomEditor(typeof(Tilemap))]
public sealed class TilemapEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (target is not Tilemap { isDestroyed: false } map) return;
        GUILayout.Label($"{map.cellCount} painted cells", EditorStyles.miniLabel);
        var bounds = map.GetBounds();
        if (map.cellLayout is TilemapLayout.HexagonPointTop or TilemapLayout.HexagonFlatTop)
            GUILayout.Label($"Hex parallel edge: {(float)map.hexSideRatio:P0}", EditorStyles.miniLabel);
        GUILayout.Label(bounds.IsEmpty ? "Empty grid" : $"Bounds: {bounds.Min} → {bounds.Max} · Anchor: {map.tileAnchor}", EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (GUILayout.Button("Relayout Prefab Cells"))
                ((TilePaletteWindowController)EditorWindow.GetWindow<TilePaletteWindow>("Tile Palette").controller).RelayoutPrefabs(map);
        if (!GUILayout.Button("Open Tile Palette")) return;
        Selection.activeGameObject = map.gameObject;
        var window = EditorWindow.GetWindow<TilePaletteWindow>("Tile Palette");
        if (map.palette is { isDestroyed: false } palette)
            ((TilePaletteWindowController)window.controller).SelectPalette(palette);
        window.Show();
    }
}
