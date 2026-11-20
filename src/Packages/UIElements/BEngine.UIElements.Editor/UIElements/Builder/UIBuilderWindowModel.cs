using BEngine.Editor;
using System.Runtime.CompilerServices;
using UnityEditor.IMGUI.Controls;

namespace BEngine.UIElements.Editor;

public sealed class UIBuilderWindowModel : EditorWindowModel
{
    private readonly Stack<UIBuilderSnapshot> _undo = [];
    private readonly Stack<UIBuilderSnapshot> _redo = [];
    private readonly ConditionalWeakTable<VisualElement, ElementIdentity> _elementIds = new();
    private int _nextElementId;
    private long _nextRevision;
    private long _revision;
    private long _savedRevision;

    public UIBuilderWindowModel(string windowId) : base(windowId)
    {
        DocumentRoot = UIBuilderWindow.CreateDefaultDocument();
        Selected = DocumentRoot;
    }

    public VisualElement DocumentRoot { get; internal set; }
    public VisualElement Selected { get; internal set; }
    public string AssetPath { get; internal set; } = string.Empty;
    public bool Dirty { get; internal set; }
    public bool PreviewMode => PreviewDocument is not null;
    public VisualElement CanvasRoot => PreviewDocument?.rootVisualElement ?? DocumentRoot;
    internal UIDocument? PreviewDocument { get; set; }
    public string Status { get; internal set; } = "Ready";
    public string LibrarySearch { get; set; } = string.Empty;
    public bool LibraryProjectControls { get; internal set; }
    public TreeViewState<int> HierarchyTreeState { get; } = new();
    public TreeViewState<int> LibraryTreeState { get; } = new();
    public Vector2 InspectorScroll { get; set; }
    public Vector2 StyleSheetsScroll { get; set; }
    public int SelectedStyleSheetIndex { get; internal set; } = -1;
    public Dictionary<string, bool> InspectorFoldouts { get; } = new(StringComparer.Ordinal)
    {
        ["Element"] = true,
        ["Style Class List"] = true,
        ["Inline Styles"] = true,
        ["Size"] = true,
        ["Text"] = true
    };
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    internal void PushUndo(UIBuilderSnapshot snapshot)
    {
        _undo.Push(snapshot);
        _redo.Clear();
    }

    internal bool TryPopUndo(out UIBuilderSnapshot snapshot)
    {
        if (!_undo.TryPop(out snapshot!)) return false;
        _redo.Push(Capture());
        return true;
    }

    internal bool TryPopRedo(out UIBuilderSnapshot snapshot)
    {
        if (!_redo.TryPop(out snapshot!)) return false;
        _undo.Push(Capture());
        return true;
    }

    internal void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _revision = ++_nextRevision;
        MarkSaved();
    }

    internal void MarkChanged()
    {
        _revision = ++_nextRevision;
        Dirty = _revision != _savedRevision;
    }

    internal void MarkSaved()
    {
        _savedRevision = _revision;
        Dirty = false;
    }

    internal UIBuilderSnapshot Capture()
    {
        var path = new Stack<int>();
        for (var current = Selected; current.parent is { } parent; current = parent)
            path.Push(parent.IndexOf(current));
        return new UIBuilderSnapshot(CloneDocument(DocumentRoot), path.ToArray(), _revision);
    }

    internal void Restore(UIBuilderSnapshot snapshot)
    {
        DocumentRoot = CloneDocument(snapshot.Root);
        Selected = DocumentRoot;
        foreach (var index in snapshot.SelectionPath)
        {
            if (index < 0 || index >= Selected.Children.Count) break;
            Selected = Selected.Children[index];
        }
        _revision = snapshot.Revision;
        Dirty = _revision != _savedRevision;
        SelectedStyleSheetIndex = Math.Min(SelectedStyleSheetIndex, DocumentRoot.styleSheets.Count - 1);
    }

    internal int GetElementId(VisualElement element) =>
        _elementIds.GetValue(element, _ => new ElementIdentity(Interlocked.Increment(ref _nextElementId))).Id;

    internal VisualElement? FindElement(int id) =>
        DocumentRoot.DescendantsAndSelf().FirstOrDefault(element => GetElementId(element) == id);

    internal static VisualElement CloneDocument(VisualElement source)
    {
        var clone = UxmlSerializer.DeserializeElement(UxmlSerializer.SerializeElement(source));
        CopyEditorState(source, clone);
        return clone;
    }

    private static void CopyEditorState(VisualElement source, VisualElement target)
    {
        target.style.CopyStateFrom(source.style);
        target.material = source.material;
        if (source is ScrollView scroll && target is ScrollView copy) copy.scrollOffset = scroll.scrollOffset;
        target.styleSheets.Clear();
        foreach (var sheet in source.styleSheets) target.styleSheets.Add(sheet);
        for (var index = 0; index < Math.Min(source.Children.Count, target.Children.Count); index++)
            CopyEditorState(source.Children[index], target.Children[index]);
    }

    private sealed record ElementIdentity(int Id);
}
