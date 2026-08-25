using System.Reflection;

namespace BEngine.Editor;

public abstract class Editor : ScriptableObject, IDisposable
{
    private SerializedObject? _serializedObject;
    private bool _enabled;
    public BObject target { get; internal set; } = null!;
    public BObject[] targets { get; internal set; } = [];
    public SerializedObject serializedObject => _serializedObject ??= new SerializedObject(targets);
    public bool hasUnsavedChanges { get; protected set; }
    public string saveChangesMessage { get; protected set; } = "This editor has unsaved changes.";

    public virtual void OnInspectorGUI() => DrawDefaultInspector();
    public bool DrawDefaultInspector()
    {
        serializedObject.UpdateIfRequiredOrScript();
        var changedBefore = GUI.changed;
        foreach (var property in serializedObject.GetVisibleProperties())
            EditorGUILayout.PropertyField(property, includeChildren: true);
        serializedObject.ApplyModifiedProperties();
        return GUI.changed != changedBefore;
    }
    public virtual bool HasPreviewGUI() => AssetPreview.HasPreview(target);
    public virtual void OnPreviewGUI(Rect previewArea) => AssetPreview.DrawAssetPreview(target, previewArea);
    public virtual string GetInfoString() => AssetPreview.GetInfoString(target);
    public virtual bool RequiresConstantRepaint() => false;
    public virtual bool UseDefaultMargins() => true;
    public virtual void OnSceneGUI() { }
    internal bool OnInspectorGUIInternal() =>
        EditorFeatureGuard.Invoke(this, nameof(OnInspectorGUI), OnInspectorGUI);
    internal bool HasPreviewGUIInternal()
    {
        EditorFeatureGuard.TryInvoke($"{GetType().FullName}.{nameof(HasPreviewGUI)}", HasPreviewGUI,
            false, out var result);
        return result;
    }
    internal void OnPreviewGUIInternal(Rect previewArea) =>
        EditorFeatureGuard.Invoke(this, nameof(OnPreviewGUI), () => OnPreviewGUI(previewArea));
    internal string GetInfoStringInternal()
    {
        EditorFeatureGuard.TryInvoke($"{GetType().FullName}.{nameof(GetInfoString)}", GetInfoString,
            string.Empty, out var result);
        return result;
    }
    internal bool RequiresConstantRepaintInternal()
    {
        EditorFeatureGuard.TryInvoke($"{GetType().FullName}.{nameof(RequiresConstantRepaint)}",
            RequiresConstantRepaint, false, out var result);
        return result;
    }
    internal void OnSceneGUIInternal() =>
        EditorFeatureGuard.Invoke(this, nameof(OnSceneGUI), OnSceneGUI);
    public virtual void SaveChanges()
    {
        serializedObject.ApplyModifiedProperties();
        hasUnsavedChanges = false;
    }
    public virtual void DiscardChanges()
    {
        serializedObject.Update();
        hasUnsavedChanges = false;
    }
    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }

    public static Editor CreateEditor(BObject target) => CreateEditor(target, FindEditorType(target.GetType()));

    internal static bool HasCustomEditor(Type inspectedType) => FindCustomEditorType(inspectedType) is not null;

    public static Editor CreateEditor(BObject target, Type editorType)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(editorType);
        if (!typeof(Editor).IsAssignableFrom(editorType) || editorType.IsAbstract)
        {
            throw new ArgumentException($"{editorType.FullName} is not a concrete Editor type.", nameof(editorType));
        }

        return CreateEditorInstance([target], editorType);
    }

    public static Editor CreateEditor(BObject[] targets, Type? editorType = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Length == 0) throw new ArgumentException("At least one target is required.", nameof(targets));
        editorType ??= FindEditorType(targets[0].GetType());
        return CreateEditorInstance(targets, editorType);
    }

    public static void CreateCachedEditor(BObject target, Type? editorType, ref Editor? previousEditor)
    {
        if (previousEditor is not null && ReferenceEquals(previousEditor.target, target) &&
            (editorType is null || previousEditor.GetType() == editorType)) return;
        previousEditor?.Dispose();
        previousEditor = CreateEditor(target, editorType ?? FindEditorType(target.GetType()));
    }

    public void Dispose()
    {
        if (!_enabled) return;
        _enabled = false;
        InvokeLifecycle(OnDisable, nameof(OnDisable));
        _serializedObject?.Dispose();
        _serializedObject = null;
        GC.SuppressFinalize(this);
    }

    private static Editor CreateEditorInstance(IReadOnlyList<BObject> inspectedTargets, Type editorType)
    {
        if (!typeof(Editor).IsAssignableFrom(editorType) || editorType.IsAbstract)
            throw new ArgumentException($"{editorType.FullName} is not a concrete Editor type.", nameof(editorType));

        if (!EditorFeatureGuard.TryInvoke<Editor?>(
                $"CustomEditor {editorType.FullName}.CreateInstance",
                () => (Editor)ScriptableObject.CreateInstance(editorType), null, out var editor) ||
            editor is null)
        {
            if (editorType == typeof(DefaultEditor))
                throw new InvalidOperationException("The built-in default Inspector could not be created.");
            return CreateEditorInstance(inspectedTargets, typeof(DefaultEditor));
        }
        editor.target = inspectedTargets[0];
        editor.targets = inspectedTargets.ToArray();
        editor._enabled = true;
        editor.InvokeLifecycle(editor.OnEnable, nameof(OnEnable));
        return editor;
    }

    private void InvokeLifecycle(Action callback, string callbackName)
    {
        try { EditorFeatureGuard.Invoke(this, callbackName, callback); }
        catch (ExitGUIException) { }
    }

    private static Type FindEditorType(Type inspectedType)
    {
        return FindCustomEditorType(inspectedType) ?? typeof(DefaultEditor);
    }

    private static Type? FindCustomEditorType(Type inspectedType) =>
        EditorTypeRegistry.Find(inspectedType);

    private sealed class DefaultEditor : Editor;
}
