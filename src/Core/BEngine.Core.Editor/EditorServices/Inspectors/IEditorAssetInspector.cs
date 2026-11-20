namespace BEngine.Editor;

/// <summary>Widgets used by asset inspectors without depending on a particular editor UI.</summary>
public interface IEditorInspectorGUI
{
    void Heading(string text);
    void Space(Fix64 pixels);
    void ReadOnlyText(string label, string value);
    void PropertyField(SerializedProperty property, bool includeChildren);
    void BeginChangeCheck();
    bool EndChangeCheck();
    int IntField(string label, int value);
    bool Toggle(string label, bool value);
    Enum EnumPopup(string label, Enum value);
}

/// <summary>Whether a package rendered the section and changed its target.</summary>
public readonly record struct EditorInspectorResult(bool Handled, bool Changed);

/// <summary>Asset-specific authoring contributed by the package that owns the asset.</summary>
public interface IEditorAssetInspector
{
    bool ShowImportSettings => true;
    void DrawInformation(BAsset asset, IEditorInspectorGUI gui) { }
    EditorInspectorResult DrawProperties(BAsset asset, SerializedObject serialized, IEditorInspectorGUI gui) => default;
    EditorInspectorResult DrawImportSettings(AssetImporter importer, SerializedObject serialized, IEditorInspectorGUI gui) => default;
}
