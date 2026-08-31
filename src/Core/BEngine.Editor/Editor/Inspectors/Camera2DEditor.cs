namespace BEngine.Editor;

[CustomEditor(typeof(Camera2D))]
public sealed class Camera2DEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var camera = (Camera2D)target;

        EditorGUI.BeginChangeCheck();
        var isMain = EditorGUILayout.Toggle("Main Camera", camera.isMain);
        var priority = EditorGUILayout.FloatField("Priority", (float)camera.priority);
        var clearMode = (CameraClearMode)EditorGUILayout.EnumPopup("Clear Mode", camera.clearMode);
        var backgroundColor = camera.backgroundColor;
        if (clearMode == CameraClearMode.Color)
            backgroundColor = EditorGUILayout.ColorField("Background", backgroundColor);
        var orthographicSize = (Fix64)EditorGUILayout.FloatField(
            "Orthographic Size", (float)camera.size);
        var viewportPosition = EditorGUILayout.Vector2Field("Viewport Position", camera.viewportRect.position);
        var viewportSize = EditorGUILayout.Vector2Field("Viewport Size", camera.viewportRect.size);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(camera, "Edit Camera 2D");
            camera.isMain = isMain;
            camera.priority = (Fix64)priority;
            camera.clearMode = clearMode;
            camera.backgroundColor = backgroundColor;
            camera.size = orthographicSize;
            camera.viewportRect = new Rect(
                viewportPosition.x, viewportPosition.y, viewportSize.x, viewportSize.y);
            EditorUtility.SetDirty(camera);
        }

        DrawCullingMask(camera);
    }

    private static void DrawCullingMask(Camera2D camera)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Culling Mask", GUILayout.Width(EditorGUI.labelWidth));
        if (GUILayout.Button(MaskSummary(camera.cullingMask))) ShowMaskMenu(camera);
        GUILayout.EndHorizontal();
    }

    private static void ShowMaskMenu(Camera2D camera)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Everything"), camera.cullingMask == SortingLayer.AllMask,
            () => SetMask(camera, SortingLayer.AllMask));
        menu.AddItem(new GUIContent("Nothing"), camera.cullingMask == 0,
            () => SetMask(camera, 0));
        menu.AddSeparator(string.Empty);
        foreach (var layer in SortingLayerRegistry.layers)
        {
            var value = layer.Value;
            var bit = SortingLayer.ToMask(value);
            var group = layer.IsUi ? "UI" : "World";
            var layerName = layer.Name.Replace('/', '-');
            var label = $"{group}/{layer.Index} {layerName}";
            menu.AddItem(new GUIContent(label), (camera.cullingMask & bit) != 0,
                () => SetMask(camera, camera.cullingMask ^ bit));
        }
        menu.ShowAsAdvancedDropdown();
    }

    private static void SetMask(Camera2D camera, ulong mask)
    {
        Undo.RecordObject(camera, "Edit Camera Culling Mask");
        camera.cullingMask = mask;
        EditorUtility.SetDirty(camera);
    }

    private static string MaskSummary(ulong mask)
    {
        mask &= SortingLayer.AllMask;
        if (mask == SortingLayer.AllMask) return "Everything";
        if (mask == 0) return "Nothing";
        if ((mask & (mask - 1)) == 0)
        {
            var index = System.Numerics.BitOperations.TrailingZeroCount(mask) +
                        SortingLayer.MinimumIndex;
            var layer = SortingLayer.FromIndex(index);
            return $"{index} {SortingLayerRegistry.NameOf(layer)}";
        }
        return $"{System.Numerics.BitOperations.PopCount(mask)} Layers";
    }
}
