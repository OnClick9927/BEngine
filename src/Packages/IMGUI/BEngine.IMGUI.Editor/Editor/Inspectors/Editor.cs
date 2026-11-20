using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public abstract class Editor : ScriptableObject, IDisposable
{
    private static readonly ConditionalWeakTable<Type, CallbackFeatures> FeaturesByType = new();
    private SerializedObject? _serializedObject;
    private CallbackFeatures? _callbackFeatures;
    private bool _enabled;
    private bool _disposed;
    private CallbackFeatures callbackFeatures =>
        _callbackFeatures ??= FeaturesByType.GetValue(GetType(), static type => new CallbackFeatures(type));
    public BObject target { get; internal set; } = null!;
    public BObject[] targets { get; internal set; } = [];
    public SerializedObject serializedObject => _serializedObject ??= new SerializedObject(targets);
    public bool hasUnsavedChanges { get; protected set; }
    public string saveChangesMessage { get; protected set; } = "This editor has unsaved changes.";

    public virtual void OnInspectorGUI() => DrawDefaultInspector();
    public bool DrawDefaultInspector()
    {
        if (targets.Length == 0 || Matches1058())
            return false;
        [global::BEngine.SkipLoopGuard]
        bool Matches1058()
        {
            var matchSource1058 = targets;
            ArgumentNullException.ThrowIfNull(matchSource1058, "source");
            global::System.ReadOnlySpan<global::BEngine.BObject> matchValues1058 = matchSource1058;
            foreach (var item in matchValues1058)
            {
                if (item.isDestroyed)
                    return true;
            }
        
            return false;
        }
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
        !target.isDestroyed && EditorCallbacks.Invoke(callbackFeatures.OnInspectorGUI, this,
            static editor => editor.OnInspectorGUI());
    internal bool HasPreviewGUIInternal()
    {
        EditorCallbacks.TryInvoke(callbackFeatures.HasPreviewGUI, this,
            static editor => editor.HasPreviewGUI(), false, out var result);
        return result;
    }
    internal void OnPreviewGUIInternal(Rect previewArea) =>
        EditorCallbacks.Invoke(callbackFeatures.OnPreviewGUI, (Editor: this, Area: previewArea),
            static state => state.Editor.OnPreviewGUI(state.Area));
    internal string GetInfoStringInternal()
    {
        EditorCallbacks.TryInvoke(callbackFeatures.GetInfoString, this,
            static editor => editor.GetInfoString(), string.Empty, out var result);
        return result;
    }
    internal bool RequiresConstantRepaintInternal()
    {
        EditorCallbacks.TryInvoke(callbackFeatures.RequiresConstantRepaint, this,
            static editor => editor.RequiresConstantRepaint(), false, out var result);
        return result;
    }
    internal void OnSceneGUIInternal() =>
        EditorCallbacks.Invoke(callbackFeatures.OnSceneGUI, this, static editor => editor.OnSceneGUI());
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

    protected override void ReleaseAssetResources() { Dispose(); base.ReleaseAssetResources(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_enabled)
        {
            _enabled = false;
            InvokeLifecycle(OnDisable, nameof(OnDisable));
        }
        _serializedObject?.Dispose();
        _serializedObject = null;
        DestroyImmediate(this);
        GC.SuppressFinalize(this);
    }

    private static Editor CreateEditorInstance(IReadOnlyList<BObject> inspectedTargets, Type editorType)
    {
        if (!typeof(Editor).IsAssignableFrom(editorType) || editorType.IsAbstract)
            throw new ArgumentException($"{editorType.FullName} is not a concrete Editor type.", nameof(editorType));

        if (!EditorCallbacks.TryInvoke<Editor?>(
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
        try { EditorCallbacks.Invoke(this, callbackName, callback); }
        catch (ExitGUIException) { }
    }

    private static Type FindEditorType(Type inspectedType)
    {
        return FindCustomEditorType(inspectedType) ?? typeof(DefaultEditor);
    }

    private static Type? FindCustomEditorType(Type inspectedType) =>
        EditorTypeRegistry.Find(inspectedType);

    // Strings contain no Type/delegate references; the weak key allows hot-reloaded
    // editor assemblies to unload after their editor instances have been released.
    private sealed class CallbackFeatures(Type type)
    {
        public readonly string OnInspectorGUI = $"{type.FullName ?? type.Name}.{nameof(Editor.OnInspectorGUI)}";
        public readonly string HasPreviewGUI = $"{type.FullName}.{nameof(Editor.HasPreviewGUI)}";
        public readonly string OnPreviewGUI = $"{type.FullName ?? type.Name}.{nameof(Editor.OnPreviewGUI)}";
        public readonly string GetInfoString = $"{type.FullName}.{nameof(Editor.GetInfoString)}";
        public readonly string RequiresConstantRepaint = $"{type.FullName}.{nameof(Editor.RequiresConstantRepaint)}";
        public readonly string OnSceneGUI = $"{type.FullName ?? type.Name}.{nameof(Editor.OnSceneGUI)}";
    }

    private sealed class DefaultEditor : Editor;
}
