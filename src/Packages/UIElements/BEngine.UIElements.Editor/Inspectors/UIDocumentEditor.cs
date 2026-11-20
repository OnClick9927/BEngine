using BEngine.Editor;

namespace BEngine.UIElements.Editor;

[CustomEditor(typeof(UIDocument)), CanEditMultipleObjects]
public sealed class UIDocumentEditor : BEngine.Editor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (GUILayout.Button("Rebuild Visual Tree"))
            {
                var __typedSource328 = targets;
                ArgumentNullException.ThrowIfNull(__typedSource328, "source");
                foreach (var __typedItem328 in __typedSource328)
                {
                    if (__typedItem328 is not UIDocument document)
                        continue;
                    document.Reload();
                }
            }
        if (target is UIDocument { visualTreeAsset: { } source } && GUILayout.Button("Open UI Builder"))
            AssetDatabase.OpenAsset(source);
    }
}
