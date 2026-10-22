using UnityEditor.IMGUI.Controls;

namespace BEngine.Editor;

/// <summary>Multi-column method hierarchy for one captured profiler frame.</summary>
internal sealed class ProfilerHierarchyTreeView : TreeView<int>
{
    private const int RootIdentifier = 1;
    private const int EditorRootIdentifier = -1;
    private const int RuntimeRootIdentifier = -2;
    private const int MethodIdentifierOffset = 1000;
    private readonly MultiColumnHeader _header;
    private readonly Action<EditorProfilerMethodSample?>? _selectionChanged;
    private readonly Dictionary<int, SampleItem> _itemsById = [];
    private EditorProfilerFrame? _frame;

    internal ProfilerHierarchyTreeView(
        TreeViewState<int> state,
        Action<EditorProfilerMethodSample?>? selectionChanged = null)
        : this(state, CreateHeader(), selectionChanged)
    {
    }

    private ProfilerHierarchyTreeView(
        TreeViewState<int> state,
        MultiColumnHeader header,
        Action<EditorProfilerMethodSample?>? selectionChanged)
        : base(state, header)
    {
        _header = header;
        _selectionChanged = selectionChanged;
        rowHeight = 22;
        depthIndentWidth = 14;
        columnIndexForTreeFoldouts = (int)Column.Overview;
        showAlternatingRowBackgrounds = true;
        showBorder = false;
        _header.sortingChanged += _ => Reload();
        Reload();
        ExpandAll();
    }

    internal void SetFrame(EditorProfilerFrame? frame)
    {
        if (_frame is null && frame is null) return;
        if (_frame is { } current && frame is { } next && current.Equals(next)) return;
        _frame = frame;
        Reload();
        ExpandAll();
    }

    protected override TreeViewItem<int> BuildRoot()
    {
        _itemsById.Clear();
        var hidden = new SampleItem(0, -1, "Profiler", 0, 0, 0, 0, null, isRoot: true);
        if (_frame is not { } frame) return hidden;

        var runtimeMilliseconds = Math.Min(frame.FrameMilliseconds, frame.RuntimeMilliseconds);
        var editorMilliseconds = Math.Max(0, frame.FrameMilliseconds - runtimeMilliseconds);
        var root = new SampleItem(RootIdentifier, 0, "EditorLoop", frame.FrameMilliseconds,
            frame.FrameMilliseconds, 1, frame.ManagedAllocatedBytes, null, isRoot: true);
        var editor = new SampleItem(EditorRootIdentifier, 1, "Editor", editorMilliseconds,
            editorMilliseconds, 1, 0, null, isRoot: true);
        var runtime = new SampleItem(RuntimeRootIdentifier, 1, "Runtime", runtimeMilliseconds,
            runtimeMilliseconds, 1, 0, null, isRoot: true);
        hidden.AddChild(root);
        root.AddChild(editor);
        root.AddChild(runtime);
        _itemsById[root.id] = root;
        _itemsById[editor.id] = editor;
        _itemsById[runtime.id] = runtime;

        if (frame.MethodSamples.Length == 0)
        {
            AddFallbackSamples(editor, runtime, frame);
            NormalizeDepth(root, 0);
            return hidden;
        }

        var methods = new Dictionary<int, SampleItem>();
        foreach (var sample in frame.MethodSamples)
        {
            var item = new SampleItem(
                MethodIdentifierOffset + sample.Id,
                1,
                sample.DisplayName,
                sample.TotalMilliseconds,
                sample.SelfMilliseconds,
                sample.Calls,
                sample.AllocatedBytes,
                sample);
            methods[sample.Id] = item;
            _itemsById[item.id] = item;
        }

        foreach (var sample in frame.MethodSamples)
        {
            var item = methods[sample.Id];
            var domainRoot = sample.Domain == EditorProfilerDomain.Editor ? editor : runtime;
            if (sample.ParentId > 0 && methods.TryGetValue(sample.ParentId, out var parent) &&
                parent.MethodSample?.Domain == sample.Domain)
                parent.AddChild(item);
            else
                domainRoot.AddChild(item);
        }

        NormalizeDepth(root, 0);
        if (_header.sortedColumnIndex >= 0)
        {
            SortChildren(editor);
            SortChildren(runtime);
        }
        return hidden;
    }

    protected override bool CanMultiSelect(TreeViewItem<int> item) => false;
    protected override bool CanRename(TreeViewItem<int> item) => false;

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        var sample = selectedIds.Count > 0 && _itemsById.TryGetValue(selectedIds[0], out var item)
            ? item.MethodSample
            : null;
        _selectionChanged?.Invoke(sample);
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        if (args.item is not SampleItem item || _frame is not { } frame)
        {
            base.RowGUI(args);
            return;
        }

        for (var visibleIndex = 0; visibleIndex < args.GetNumVisibleColumns(); visibleIndex++)
        {
            var column = (Column)args.GetColumn(visibleIndex);
            var cell = args.GetCellRect(visibleIndex);
            cell = new Rect(cell.x + 5, cell.y, Fix64.Max(0, cell.width - 10), cell.height);
            CenterRectUsingSingleLineHeight(ref cell);
            if (column == Column.Overview)
            {
                var indent = (Fix64)GetContentIndent(item);
                cell = new Rect(cell.x + indent, cell.y, Fix64.Max(0, cell.width - indent), cell.height);
            }

            var text = column switch
            {
                Column.Overview => item.displayName,
                Column.TotalPercent => FormatPercent(item.Milliseconds, frame.FrameMilliseconds),
                Column.SelfPercent => FormatPercent(item.SelfMilliseconds, frame.FrameMilliseconds),
                Column.Calls => item.Calls.ToString("N0"),
                Column.GcAlloc => FormatBytes(item.AllocatedBytes),
                Column.TimeMilliseconds => $"{item.Milliseconds:F3}",
                Column.SelfMilliseconds => $"{item.SelfMilliseconds:F3}",
                _ => string.Empty
            };
            GUI.Label(cell, text,
                column == Column.Overview && item.IsRoot ? EditorStyles.boldLabel : EditorStyles.label);
        }
    }

    private void AddFallbackSamples(SampleItem editor, SampleItem runtime, EditorProfilerFrame frame)
    {
        var samples = new[]
        {
            new SampleItem(-10, 1, "Editor.Update", frame.UpdateMilliseconds,
                frame.UpdateMilliseconds, 1, 0, null),
            new SampleItem(-11, 1, "Editor.Rendering", frame.RenderMilliseconds,
                frame.RenderMilliseconds, 1, 0, null),
            new SampleItem(-12, 1, "Editor.IMGUI", frame.ImGuiMilliseconds,
                frame.ImGuiMilliseconds, 1, 0, null),
            new SampleItem(-13, 1, "Editor.Present", frame.PresentMilliseconds,
                frame.PresentMilliseconds, 1, 0, null),
            new SampleItem(-14, 1, "Scripts / Runtime.PlayerLoop", frame.RuntimeMilliseconds,
                frame.RuntimeMilliseconds, 1, 0, null)
        };
        for (var index = 0; index < samples.Length - 1; index++) editor.AddChild(samples[index]);
        runtime.AddChild(samples[^1]);
        foreach (var item in samples) _itemsById[item.id] = item;
    }

    private void SortChildren(SampleItem parent)
    {
        if (parent.children is not { Count: > 0 }) return;
        var sorted = parent.children.OfType<SampleItem>().ToList();
        sorted.Sort(CompareSamples);
        parent.children = sorted.Cast<TreeViewItem<int>>().ToList();
        foreach (var child in parent.children.OfType<SampleItem>()) SortChildren(child);
    }

    private int CompareSamples(SampleItem left, SampleItem right)
    {
        var ascending = _header.IsSortedAscending(_header.sortedColumnIndex);
        var comparison = ((Column)_header.sortedColumnIndex) switch
        {
            Column.Overview => string.Compare(left.displayName, right.displayName,
                StringComparison.OrdinalIgnoreCase),
            Column.Calls => left.Calls.CompareTo(right.Calls),
            Column.GcAlloc => left.AllocatedBytes.CompareTo(right.AllocatedBytes),
            Column.SelfPercent or Column.SelfMilliseconds =>
                left.SelfMilliseconds.CompareTo(right.SelfMilliseconds),
            _ => left.Milliseconds.CompareTo(right.Milliseconds)
        };
        return ascending ? comparison : -comparison;
    }

    private static void NormalizeDepth(SampleItem parent, int depth)
    {
        parent.depth = depth;
        if (parent.children is null) return;
        foreach (var child in parent.children.OfType<SampleItem>()) NormalizeDepth(child, depth + 1);
    }

    private static MultiColumnHeader CreateHeader()
    {
        var state = new MultiColumnHeaderState(
        [
            NewColumn("Overview", 270, 130, 620, TextAnchor.MiddleLeft),
            NewColumn("Total", 66, 52, 100, TextAnchor.MiddleRight),
            NewColumn("Self", 66, 52, 100, TextAnchor.MiddleRight),
            NewColumn("Calls", 58, 44, 90, TextAnchor.MiddleRight),
            NewColumn("GC Alloc", 86, 62, 130, TextAnchor.MiddleRight),
            NewColumn("Time ms", 78, 58, 120, TextAnchor.MiddleRight),
            NewColumn("Self ms", 78, 58, 120, TextAnchor.MiddleRight)
        ]);
        var header = new MultiColumnHeader(state) { canSort = true };
        header.SetSorting((int)Column.TimeMilliseconds, ascending: false);
        return header;
    }

    private static MultiColumnHeaderState.Column NewColumn(string label, int width, int minimum,
        int maximum, TextAnchor alignment) => new()
    {
        headerContent = new GUIContent(label),
        headerTextAlignment = alignment,
        width = width,
        minWidth = minimum,
        maxWidth = maximum,
        autoResize = true,
        allowToggleVisibility = true,
        canSort = true
    };

    private static string FormatPercent(double value, double total) =>
        total <= 0 ? "0.0%" : $"{Math.Clamp(value / total * 100d, 0, 999.9):F1}%";

    private static string FormatBytes(long bytes)
    {
        bytes = Math.Max(0, bytes);
        return bytes switch
        {
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
            _ => $"{bytes} B"
        };
    }

    private enum Column
    {
        Overview,
        TotalPercent,
        SelfPercent,
        Calls,
        GcAlloc,
        TimeMilliseconds,
        SelfMilliseconds
    }

    private sealed class SampleItem(
        int id,
        int depth,
        string name,
        double milliseconds,
        double selfMilliseconds,
        int calls,
        long allocatedBytes,
        EditorProfilerMethodSample? methodSample,
        bool isRoot = false) : TreeViewItem<int>(id, depth, name)
    {
        internal double Milliseconds { get; } = milliseconds;
        internal double SelfMilliseconds { get; } = selfMilliseconds;
        internal int Calls { get; } = calls;
        internal long AllocatedBytes { get; } = allocatedBytes;
        internal EditorProfilerMethodSample? MethodSample { get; } = methodSample;
        internal bool IsRoot { get; } = isRoot;
    }
}
