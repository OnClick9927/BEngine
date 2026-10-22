using BEngine.Editor;

namespace BEngine.UIElements.Editor;

[CustomEditor(typeof(UIDocument)), CanEditMultipleObjects]
public sealed class UIDocumentEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (GUILayout.Button("Rebuild Visual Tree"))
            foreach (var document in targets.OfType<UIDocument>()) document.Reload();
        if (target is UIDocument { visualTreeAsset: { } source } && GUILayout.Button("Open UI Builder"))
            AssetDatabase.OpenAsset(source);
    }
}
