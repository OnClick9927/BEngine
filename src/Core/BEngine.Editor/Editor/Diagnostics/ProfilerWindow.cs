using UnityEditor.IMGUI.Controls;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
internal sealed class ProfilerWindow : EditorWindow
{
    private const int CompactToolbarThreshold = 780;
    private const int CompactModuleThreshold = 500;
    private const int ToolbarRowHeight = 24;
    private const int DetailsToolbarHeight = 24;
    private const int SplitterHeight = 4;
    private const int MinimumGraphHeight = 84;
    private const int MinimumDetailsHeight = 70;
    private const int ModuleRowHeight = 132;
    private const string CpuModuleId = "builtin.cpu";
    private const string RenderingModuleId = "builtin.rendering";
    private const string MemoryModuleId = "builtin.memory";
    private static readonly Color CpuUpdateColor = Rgb(93, 148, 60);
    private static readonly Color CpuScriptColor = Rgb(49, 137, 156);
    private static readonly Color CpuRenderColor = Rgb(174, 108, 38);
    private static readonly Color CpuImGuiColor = Rgb(102, 75, 135);
    private static readonly Color CpuPresentColor = Rgb(127, 119, 40);
    private static readonly Color CpuOtherColor = Rgb(86, 86, 86);
    private static readonly Color RenderingBatchColor = Rgb(103, 151, 46);
    private static readonly Color RenderingDrawColor = Rgb(44, 139, 155);
    private static readonly Color RenderingTriangleColor = Rgb(190, 111, 34);
    private static readonly Color RenderingVertexColor = Rgb(125, 91, 152);
    private static readonly Color MemoryHeapColor = Rgb(147, 154, 37);
    private static readonly Color MemoryWorkingColor = Rgb(46, 139, 158);
    private static readonly Color MemoryPrivateColor = Rgb(180, 106, 41);
    private static readonly Color MemoryAllocationColor = Rgb(113, 86, 153);
    private static int s_openWindowCount;

    private readonly TreeViewState<int> _hierarchyState = new();
    private readonly TreeViewState<int> _moduleTreeState = new();
    private readonly TreeViewState<int> _counterTreeState = new();
    private readonly HashSet<string> _visibleModuleIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownModuleIds = new(StringComparer.Ordinal);
    private ProfilerHierarchyTreeView? _hierarchy;
    private ProfilerModuleTreeView? _moduleTree;
    private ProfilerCounterTreeView? _counterTree;
    private EditorProfilerMethodSample? _selectedMethodSample;
    private ProfilerCounterDetail? _selectedCounterDetail;
    private long _observedVersion = -1;
    private long _selectedFrameIndex = -1;
    private bool _followLatest = true;
    private bool _clearOnPlay;
    private bool _countedAsOpen;
    private bool _snapshotRecording;
    private int _snapshotCapacity;
    private int _frameCount;
    private Fix64 _graphHeightRatio = Fix64.FromDecimal(0.54m);
    private Fix64 _splitterDragStartY;
    private Fix64 _splitterDragStartHeight;
    private ProfilerModule _module;
    private string? _externalModuleId;
    private DetailsView _detailsView;
    private EditorProfilerFrame[] _frames = [];
    private Vector2 _detailsScroll;
    private Vector2 _callStackScroll;

    [MenuItem("Window/Analysis/Profiler", false, 211)]
    private static void Open() => GetWindow<ProfilerWindow>("Profiler");

    protected override void OnEnable()
    {
        minSize = new Vector2(300, 240);
        titleContent = new GUIContent("Profiler", "Icons/Windows/Window.png",
            "Inspect editor CPU, rendering, and memory history");
        _hierarchy ??= new ProfilerHierarchyTreeView(_hierarchyState,
            sample => _selectedMethodSample = sample);
        _moduleTree ??= new ProfilerModuleTreeView(_moduleTreeState, SelectModule,
            DrawModuleTreeRow, ModuleRowHeight);
        _counterTree ??= new ProfilerCounterTreeView(_counterTreeState,
            detail => _selectedCounterDetail = detail);
        EditorProfilerModuleRegistry.modulesChanged -= OnProfilerModulesChanged;
        EditorProfilerModuleRegistry.modulesChanged += OnProfilerModulesChanged;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        if (!_countedAsOpen)
        {
            _countedAsOpen = true;
            if (Interlocked.Increment(ref s_openWindowCount) == 1)
                EditorProfiler.Recording = true;
        }
        RefreshSnapshot(force: true);
    }

    protected override void OnDisable()
    {
        EditorProfilerModuleRegistry.modulesChanged -= OnProfilerModulesChanged;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        if (!_countedAsOpen) return;
        _countedAsOpen = false;
        if (Interlocked.Decrement(ref s_openWindowCount) <= 0)
        {
            Interlocked.Exchange(ref s_openWindowCount, 0);
            EditorProfiler.Recording = false;
        }
    }

    protected override void Update() => RefreshSnapshot(force: false);

    protected override void OnGUI()
    {
        RefreshSnapshot(force: false);
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight);
        var toolbarHeight = DrawToolbar(width);
        var content = new Rect(0, toolbarHeight, width, Fix64.Max(1, height - toolbarHeight));
        if (_frameCount == 0)
        {
            DrawEmptyState(content, _snapshotRecording
                ? "Recording profiler data..."
                : "Press Record to collect editor performance data.");
            return;
        }

        var maximumGraphHeight = Fix64.Max(MinimumGraphHeight,
            content.height - MinimumDetailsHeight - SplitterHeight);
        var graphHeight = Fix64.Clamp(content.height * _graphHeightRatio,
            Fix64.Min(MinimumGraphHeight, maximumGraphHeight), maximumGraphHeight);
        var graphRect = new Rect(content.x, content.y, content.width, graphHeight);
        DrawModules(graphRect);

        var splitter = new Rect(content.x, graphRect.yMax, content.width, SplitterHeight);
        graphHeight = HandleGraphSplitter(splitter, content, graphHeight);
        GUI.DrawRect(splitter, EditorStyles.separator.normal.backgroundColor);
        var details = new Rect(content.x, content.y + graphHeight + SplitterHeight, content.width,
            Fix64.Max(1, content.height - graphHeight - SplitterHeight));
        DrawDetails(details);
    }

    private Fix64 DrawToolbar(Fix64 width)
    {
        var compact = width < CompactToolbarThreshold;
        var veryCompact = width < CompactModuleThreshold;
        var height = compact ? ToolbarRowHeight * 2 + 2 : ToolbarRowHeight + 1;
        GUI.Box(new Rect(0, 0, width, height), GUIContent.none, EditorStyles.toolbar);
        var x = (Fix64)3;
        var y = (Fix64)1;
        var recordWidth = veryCompact ? (Fix64)60 : 68;
        var recording = GUI.Toggle(new Rect(x, y, recordWidth, ToolbarRowHeight),
            EditorProfiler.Recording,
            "Record", EditorProfiler.Recording ? EditorStyles.toolbarIconButtonSelected :
            EditorStyles.toolbarToggle);
        if (recording != EditorProfiler.Recording) EditorProfiler.Recording = recording;
        if (EditorProfiler.Recording)
            GUI.DrawRect(new Rect(x + 5, y + 7, 8, 8), Rgb(215, 65, 65));
        x += recordWidth + 2;

        if (!veryCompact)
        {
            using (new EditorGUI.DisabledScope(true))
                GUI.Button(new Rect(x, y, 90, ToolbarRowHeight), "Editor",
                    EditorStyles.toolbarDropDown);
            x += 92;
            if (ToolbarButton(ref x, y, 28, "|<")) SelectFirst();
        }
        if (ToolbarButton(ref x, y, 28, "<")) SelectRelative(-1);
        if (ToolbarButton(ref x, y, 28, ">")) SelectRelative(1);
        if (!veryCompact && ToolbarButton(ref x, y, 28, ">|"))
        {
            _followLatest = true;
            SelectLatest();
        }
        var selectedPosition = SelectedFramePosition();
        var frameLabelWidth = veryCompact ? Fix64.Max(50, width - x - 4) : (Fix64)112;
        GUI.Label(new Rect(x + 3, y, frameLabelWidth, ToolbarRowHeight),
            selectedPosition >= 0 ? $"Frame: {selectedPosition + 1} / {_frameCount}" : "Frame: -",
            EditorStyles.toolbarLabel);
        x += frameLabelWidth + 4;

        if (compact)
        {
            x = 3;
            y += ToolbarRowHeight + 1;
        }
        if (ToolbarButton(ref x, y, 48, "Clear")) ClearFrames();
        _clearOnPlay = GUI.Toggle(new Rect(x, y, 98, ToolbarRowHeight), _clearOnPlay,
            "Clear on Play", EditorStyles.toolbarToggle);
        x += 100;
        if (width - x >= 122)
        {
            var modulesRect = new Rect(x, y, 120, ToolbarRowHeight);
            if (GUI.Button(modulesRect, "Profiler Modules", EditorStyles.toolbarDropDown))
                ShowProfilerModulesMenu(modulesRect);
            x += 122;
        }
        using (new EditorGUI.DisabledScope(true))
        {
            if (width - x >= 92)
            {
                GUI.Button(new Rect(x, y, 92, ToolbarRowHeight),
                    new GUIContent("Deep Profile",
                        "Marker-level deep profiling is not available yet."),
                    EditorStyles.toolbarButton);
                x += 94;
            }
        }
        if (width - x >= 92)
            EditorProfiler.CaptureCallStacks = GUI.Toggle(
                new Rect(x, y, 90, ToolbarRowHeight),
                EditorProfiler.CaptureCallStacks,
                new GUIContent("Call Stacks",
                    "Capture managed method and allocation call stacks. This adds profiling overhead."),
                EditorStyles.toolbarToggle);
        return height;
    }

    private void DrawModules(Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var modules = VisibleModules();
        if (modules.Length == 0)
        {
            DrawEmptyState(area, "Enable at least one item in Profiler Modules.");
            return;
        }
        _moduleTree ??= new ProfilerModuleTreeView(_moduleTreeState, SelectModule,
            DrawModuleTreeRow, ModuleRowHeight);
        _moduleTree.SetModules(modules, SelectedModuleId());
        _moduleTree.OnGUI(new Rect(area.x + 1, area.y + 1,
            Fix64.Max(1, area.width - 2), Fix64.Max(1, area.height - 2)));
    }

    private void DrawModuleTreeRow(Rect row, ProfilerModuleEntry entry)
    {
        var summaryWidth = row.width < CompactModuleThreshold
            ? Fix64.Clamp(row.width * Fix64.FromDecimal(0.34m), 92, 132)
            : Fix64.Clamp(row.width * Fix64.FromDecimal(0.23m), 180, 270);
        var summary = new Rect(row.x, row.y, summaryWidth, row.height);
        var graph = new Rect(summary.xMax, row.y, Fix64.Max(1, row.width - summaryWidth), row.height);
        if (entry.BuiltIn is { } builtIn)
        {
            DrawModuleSummary(summary, builtIn);
            DrawModuleGraph(graph, builtIn);
        }
        else if (entry.External is { } external)
        {
            DrawExternalModuleSummary(summary, external);
            DrawExternalModuleGraph(graph, external);
        }
        GUI.DrawRect(new Rect(summary.xMax, row.y, 1, row.height),
            EditorStyles.separator.normal.backgroundColor);
        GUI.DrawRect(new Rect(row.x, row.yMax - 1, row.width, 1),
            EditorStyles.separator.normal.backgroundColor);
    }

    private void DrawModuleSummary(Rect area, ProfilerModule module)
    {
        GUI.Box(area, GUIContent.none, _externalModuleId is null && module == _module
            ? EditorStyles.treeViewRowSelected :
            EditorStyles.treeViewRow);
        GUI.Label(new Rect(area.x + 7, area.y + 2, Fix64.Max(1, area.width - 14), 20),
            ModuleName(module), EditorStyles.boldLabel);
        if (SelectedFrame() is { } selectedFrame && area.height >= 54)
            DrawLegend(area, module, selectedFrame);
    }

    private void DrawExternalModuleSummary(Rect area, EditorProfilerModuleDefinition module)
    {
        GUI.Box(area, GUIContent.none,
            string.Equals(_externalModuleId, module.Id, StringComparison.Ordinal)
                ? EditorStyles.treeViewRowSelected
                : EditorStyles.treeViewRow);
        GUI.Label(new Rect(area.x + 7, area.y + 2, Fix64.Max(1, area.width - 14), 20),
            new GUIContent(module.DisplayName, module.Icon), EditorStyles.boldLabel);
        if (SelectedFrame() is { } frame && area.height >= 54)
        {
            var entries = module.Counters.Take(Math.Max(1, (int)((area.height - 25) / 17))).ToArray();
            for (var index = 0; index < entries.Length; index++)
            {
                var y = area.y + 24 + index * 17;
                GUI.DrawRect(new Rect(area.x + 8, y + 6, 7, 7), entries[index].Color);
                GUI.Label(new Rect(area.x + 19, y, Fix64.Max(1, area.width - 25), 17),
                    $"{entries[index].DisplayName} {FormatCounterValue(
                        CounterValue(frame, module.Id, entries[index].Name), entries[index].Unit)}",
                    EditorStyles.miniLabel);
            }
        }
    }

    private static void DrawLegend(Rect area, ProfilerModule module, EditorProfilerFrame frame)
    {
        var entries = LegendEntries(module, frame);
        var columns = area.width >= 210 ? 2 : 1;
        var availableRows = Math.Max(1, (int)((area.height - 25) / 17));
        var maximumEntries = Math.Min(entries.Length, availableRows * columns);
        var columnWidth = area.width / columns;
        for (var index = 0; index < maximumEntries; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var x = area.x + columnWidth * column + 8;
            var y = area.y + 24 + row * 17;
            GUI.DrawRect(new Rect(x, y + 6, 7, 7), entries[index].Color);
            GUI.Label(new Rect(x + 11, y, Fix64.Max(1, columnWidth - 17), 17),
                $"{entries[index].Name} {entries[index].Value}", EditorStyles.miniLabel);
        }
    }

    private void DrawModuleGraph(Rect area, ProfilerModule module)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var plot = new Rect(area.x + 4, area.y + 3, Fix64.Max(1, area.width - 8),
            Fix64.Max(1, area.height - 7));
        switch (module)
        {
            case ProfilerModule.Cpu:
                DrawCpuGraph(plot);
                break;
            case ProfilerModule.Rendering:
                DrawRenderingGraph(plot);
                break;
            case ProfilerModule.Memory:
                DrawMemoryGraph(plot);
                break;
        }
        DrawSelectedFrameLine(plot);
        HandleGraphClick(plot, module);
    }

    private void DrawExternalModuleGraph(Rect area, EditorProfilerModuleDefinition module)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var plot = new Rect(area.x + 4, area.y + 3, Fix64.Max(1, area.width - 8),
            Fix64.Max(1, area.height - 7));
        var maximum = Math.Max(1, Maximum(frame => module.Counters.Max(counter =>
            CounterValue(frame, module.Id, counter.Name))));
        foreach (var counter in module.Counters)
            DrawSeries(plot, frame => CounterValue(frame, module.Id, counter.Name),
                maximum, counter.Color);
        GUI.Label(new Rect(plot.x + 3, plot.y, Fix64.Max(1, plot.width - 6), 18),
            maximum.ToString("N2"), EditorStyles.miniLabel);
        DrawSelectedFrameLine(plot);
        HandleExternalGraphClick(plot, module.Id);
    }

    private void DrawCpuGraph(Rect plot)
    {
        var maximum = Math.Max(66.67, Maximum(static frame => frame.FrameMilliseconds));
        DrawCpuGuide(plot, maximum, 16.67, "16ms (60FPS)");
        DrawCpuGuide(plot, maximum, 33.33, "33ms (30FPS)");
        DrawCpuGuide(plot, maximum, 66.67, "66ms (15FPS)");
        var range = VisibleFrameRange(plot);
        if (range.Count <= 0) return;
        var step = plot.width / range.Count;
        var barWidth = Fix64.Max(1, step - 1);
        for (var visible = 0; visible < range.Count; visible++)
        {
            var frame = _frames[range.Start + visible];
            var x = plot.x + step * visible;
            var y = plot.yMax;
            foreach (var sample in CpuSamples(frame))
            {
                var sampleHeight = Fix64.Max(0,
                    plot.height * (Fix64)Math.Clamp(sample.Value / maximum, 0, 1));
                y -= sampleHeight;
                if (sampleHeight > 0)
                    GUI.DrawRect(new Rect(x, y, barWidth, sampleHeight), sample.Color);
            }
        }
    }

    private void DrawRenderingGraph(Rect plot)
    {
        var statisticsMaximum = Math.Max(1, Maximum(static frame => Math.Max(
            Math.Max(frame.RenderStatistics.VertexCount, frame.RenderStatistics.TriangleCount),
            Math.Max(frame.RenderStatistics.BatchCount, frame.RenderStatistics.DrawCallCount))));
        DrawSeries(plot, static frame => frame.RenderStatistics.BatchCount, statisticsMaximum,
            RenderingBatchColor);
        DrawSeries(plot, static frame => frame.RenderStatistics.DrawCallCount, statisticsMaximum,
            RenderingDrawColor);
        DrawSeries(plot, static frame => frame.RenderStatistics.TriangleCount, statisticsMaximum,
            RenderingTriangleColor);
        DrawSeries(plot, static frame => frame.RenderStatistics.VertexCount, statisticsMaximum,
            RenderingVertexColor);
        GUI.Label(new Rect(plot.x + 3, plot.y, plot.width - 6, 18),
            statisticsMaximum.ToString("N0"), EditorStyles.miniLabel);
    }

    private void DrawMemoryGraph(Rect plot)
    {
        var maximum = Math.Max(1, Maximum(static frame => Math.Max(
            Math.Max(frame.ManagedHeapBytes, frame.ManagedAllocatedBytes),
            Math.Max(frame.WorkingSetBytes, frame.PrivateBytes))));
        DrawSeries(plot, static frame => frame.ManagedHeapBytes, maximum, MemoryHeapColor);
        DrawSeries(plot, static frame => frame.WorkingSetBytes, maximum, MemoryWorkingColor);
        DrawSeries(plot, static frame => frame.PrivateBytes, maximum, MemoryPrivateColor);
        DrawSeries(plot, static frame => frame.ManagedAllocatedBytes, maximum, MemoryAllocationColor);
        GUI.Label(new Rect(plot.x + 3, plot.y, plot.width - 6, 18), FormatBytes((long)maximum),
            EditorStyles.miniLabel);
    }

    private void DrawSeries(Rect plot, Func<EditorProfilerFrame, double> value,
        double maximum, Color color)
    {
        var range = VisibleFrameRange(plot);
        if (range.Count <= 0) return;
        var step = plot.width / range.Count;
        for (var visible = 0; visible < range.Count; visible++)
        {
            var normalized = Math.Clamp(value(_frames[range.Start + visible]) / maximum, 0, 1);
            var x = plot.x + step * visible;
            var y = plot.yMax - plot.height * (Fix64)normalized;
            GUI.DrawRect(new Rect(x, y - 1, Fix64.Max(2, step), 2), color);
        }
    }

    private void DrawCpuGuide(Rect plot, double maximum, double value, string label)
    {
        if (value > maximum) return;
        var y = plot.yMax - plot.height * (Fix64)(value / maximum);
        GUI.DrawRect(new Rect(plot.x, y, plot.width, 1),
            new Color(Fix64.FromDecimal(0.35m), Fix64.FromDecimal(0.35m),
                Fix64.FromDecimal(0.35m), Fix64.One));
        if (plot.height >= 62)
            GUI.Label(new Rect(plot.x + 3, y - 17, Fix64.Max(1, plot.width - 6), 17),
                label, EditorStyles.miniLabel);
    }

    private void DrawSelectedFrameLine(Rect plot)
    {
        var range = VisibleFrameRange(plot);
        var selected = SelectedFramePosition();
        if (selected < range.Start || selected >= range.Start + range.Count || range.Count <= 0) return;
        var x = plot.x + plot.width * (selected - range.Start + Fix64.FromDecimal(0.5m)) /
            range.Count;
        GUI.DrawRect(new Rect(x, plot.y, 1, plot.height), Color.white);
    }

    private void DrawDetails(Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var toolbar = new Rect(area.x, area.y, area.width,
            Fix64.Min(DetailsToolbarHeight, area.height));
        GUI.Box(toolbar, GUIContent.none, EditorStyles.toolbar);
        DrawDetailsToolbar(toolbar);
        var body = new Rect(area.x, toolbar.yMax, area.width,
            Fix64.Max(0, area.yMax - toolbar.yMax));
        if (body.height <= 1) return;
        if (SelectedFrame() is not { } frame)
        {
            DrawEmptyState(body, "Select a frame to inspect its samples.");
            return;
        }

        if (_externalModuleId is { } externalModuleId)
        {
            var module = EditorProfilerModuleRegistry.GetModules().FirstOrDefault(item =>
                string.Equals(item.Id, externalModuleId, StringComparison.Ordinal));
            if (module is null)
            {
                _externalModuleId = null;
                DrawEmptyState(body, "The selected profiler module is no longer registered.");
            }
            else if (module.DrawDetails is { } drawDetails)
                drawDetails(body, frame);
            else
                DrawExternalCounterDetails(body, frame, module);
            return;
        }

        if (_module == ProfilerModule.Cpu)
        {
            _hierarchy ??= new ProfilerHierarchyTreeView(_hierarchyState,
                sample => _selectedMethodSample = sample);
            _hierarchy.SetFrame(frame);
            DrawCpuDetails(body, frame);
            return;
        }
        DrawCounterDetails(body, frame);
    }

    private void DrawCpuDetails(Rect area, EditorProfilerFrame frame)
    {
        if (area.height < 150)
        {
            _hierarchy!.OnGUI(area);
            return;
        }
        var callStackHeight = Fix64.Clamp(area.height * Fix64.FromDecimal(0.32m), 92, 180);
        var tree = new Rect(area.x, area.y, area.width,
            Fix64.Max(1, area.height - callStackHeight - 1));
        _hierarchy!.OnGUI(tree);
        GUI.DrawRect(new Rect(area.x, tree.yMax, area.width, 1),
            EditorStyles.separator.normal.backgroundColor);
        DrawMethodDetails(new Rect(area.x, tree.yMax + 1, area.width, callStackHeight), frame);
    }

    private void DrawMethodDetails(Rect area, EditorProfilerFrame frame)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        if (_selectedMethodSample is not { } selected)
        {
            DrawEmptyState(area, "Select a method sample to inspect its domain and call stack.");
            return;
        }
        var sample = frame.MethodSamples.FirstOrDefault(item => item.Id == selected.Id);
        if (sample.Id == 0)
        {
            DrawEmptyState(area, "Select a method sample to inspect its domain and call stack.");
            return;
        }
        var contentHeight = Fix64.Max(area.height,
            52 + Math.Max(1, sample.CallStack.Length) * 19);
        _callStackScroll = GUI.BeginScrollView(area, _callStackScroll,
            new Rect(0, 0, Fix64.Max(1, area.width - 11), contentHeight));
        try
        {
            var width = Fix64.Max(1, area.width - 11);
            GUI.Label(new Rect(8, 3, width - 16, 22),
                $"{sample.Domain} | {sample.DisplayName}", EditorStyles.boldLabel);
            GUI.Label(new Rect(8, 25, width - 16, 22),
                $"{sample.ThreadName} ({sample.ThreadId})   Total {sample.TotalMilliseconds:F3} ms   " +
                $"Self {sample.SelfMilliseconds:F3} ms   GC {FormatBytes(sample.AllocatedBytes)}",
                EditorStyles.miniLabel);
            var y = (Fix64)49;
            if (sample.CallStack.Length == 0)
                GUI.Label(new Rect(18, y, width - 26, 19),
                    "Call stack was not captured for this frame.", EditorStyles.centeredGreyMiniLabel);
            else
                for (var index = 0; index < sample.CallStack.Length; index++, y += 19)
                    GUI.Label(new Rect(18, y, width - 26, 19),
                        $"{index}: {sample.CallStack[index]}", EditorStyles.miniLabel);
        }
        finally { GUI.EndScrollView(); }
    }

    private void DrawDetailsToolbar(Rect toolbar)
    {
        var x = toolbar.x + 3;
        var cpuModule = _externalModuleId is null && _module == ProfilerModule.Cpu;
        var detailsAnchor = new Rect(x, toolbar.y + 1, 130, ToolbarRowHeight);
        if (cpuModule)
        {
            if (GUI.Button(detailsAnchor,
                    _detailsView == DetailsView.Hierarchy ? "Hierarchy" : "Raw Hierarchy",
                    EditorStyles.toolbarDropDown))
                ShowDetailsViewMenu(detailsAnchor);
        }
        else
            GUI.Label(detailsAnchor, SelectedModuleName(), EditorStyles.toolbarLabel);
        x += 133;
        var nextFollow = GUI.Toggle(new Rect(x, toolbar.y + 1, 50, ToolbarRowHeight),
            _followLatest, "Live", EditorStyles.toolbarToggle);
        if (nextFollow != _followLatest)
        {
            _followLatest = nextFollow;
            if (_followLatest) SelectLatest();
        }
        x += 53;
        if (cpuModule && toolbar.xMax - x >= 108)
        {
            using (new EditorGUI.DisabledScope(true))
                GUI.Button(new Rect(x, toolbar.y + 1, 105, ToolbarRowHeight), "Main Thread",
                    EditorStyles.toolbarDropDown);
            x += 108;
        }
        if (SelectedFrame() is { } frame && toolbar.xMax - x >= 120)
            GUI.Label(new Rect(x + 3, toolbar.y + 1, Fix64.Max(1, toolbar.xMax - x - 6),
                    ToolbarRowHeight),
                $"CPU: {frame.FrameMilliseconds:F2} ms   GC: {FormatBytes(frame.ManagedAllocatedBytes)}",
                EditorStyles.toolbarLabel);
    }

    private void ShowDetailsViewMenu(Rect anchor)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Hierarchy"), _detailsView == DetailsView.Hierarchy,
            () => _detailsView = DetailsView.Hierarchy);
        menu.AddItem(new GUIContent("Raw Hierarchy"), _detailsView == DetailsView.RawHierarchy,
            () => _detailsView = DetailsView.RawHierarchy);
        menu.AddSeparator(string.Empty);
        menu.AddDisabledItem(new GUIContent("Timeline (marker data unavailable)"));
        menu.AddDisabledItem(new GUIContent("Inverted Hierarchy (marker data unavailable)"));
        menu.DropDown(anchor);
    }

    private void DrawCounterDetails(Rect area, EditorProfilerFrame frame)
    {
        var previous = PreviousSelectedFrame();
        var details = _module == ProfilerModule.Rendering
            ? RenderingDetails(frame, previous)
            : MemoryDetails(frame, previous);
        _counterTree ??= new ProfilerCounterTreeView(_counterTreeState,
            detail => _selectedCounterDetail = detail);
        _counterTree.SetDetails(details);
        if (area.height < 150)
        {
            _counterTree.OnGUI(area);
            return;
        }

        var inspectorHeight = Fix64.Clamp(area.height * Fix64.FromDecimal(0.38m), 96, 178);
        var tree = new Rect(area.x, area.y, area.width,
            Fix64.Max(1, area.height - inspectorHeight - 1));
        _counterTree.OnGUI(tree);
        GUI.DrawRect(new Rect(area.x, tree.yMax, area.width, 1),
            EditorStyles.separator.normal.backgroundColor);
        DrawCounterInspector(new Rect(area.x, tree.yMax + 1, area.width, inspectorHeight));
    }

    private void DrawCounterInspector(Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        if (_selectedCounterDetail is not { } detail)
        {
            DrawEmptyState(area, "Select a counter to inspect its captured value.");
            return;
        }

        var width = Fix64.Max(1, area.width - 16);
        var y = area.y + 5;
        GUI.Label(new Rect(area.x + 8, y, width, 22),
            $"{detail.Category} / {detail.Name}", EditorStyles.boldLabel);
        y += 24;
        using (new EditorGUI.DisabledScope(true))
        {
            _ = EditorGUI.TextField(new Rect(area.x + 8, y, width, 22), "Category",
                detail.Category);
            y += 23;
            DrawCounterValueField(new Rect(area.x + 8, y, width, 22), detail);
        }
        y += 25;
        if (area.yMax - y >= 38)
            EditorGUI.HelpBox(new Rect(area.x + 8, y, width, area.yMax - y - 5),
                detail.Description, MessageType.Info);
    }

    private static void DrawCounterValueField(Rect row, ProfilerCounterDetail detail)
    {
        switch (detail.Kind)
        {
            case ProfilerCounterValueKind.Integer when detail.IntegerValue is >= int.MinValue and
                <= int.MaxValue:
                _ = EditorGUI.IntField(row, "Value", (int)detail.IntegerValue);
                break;
            case ProfilerCounterValueKind.Milliseconds:
            case ProfilerCounterValueKind.Percentage:
            case ProfilerCounterValueKind.Decimal:
                _ = EditorGUI.FloatField(row, "Value", (float)detail.NumericValue);
                break;
            case ProfilerCounterValueKind.Boolean:
                _ = EditorGUI.Toggle(row, "Value", detail.BooleanValue);
                break;
            case ProfilerCounterValueKind.Bytes:
            case ProfilerCounterValueKind.Integer:
                _ = EditorGUI.TextField(row, "Value", detail.IntegerValue.ToString("N0"));
                break;
            default:
                _ = EditorGUI.TextField(row, "Value",
                    string.IsNullOrWhiteSpace(detail.TextValue)
                        ? detail.DisplayValue
                        : detail.TextValue);
                break;
        }
    }

    private void DrawExternalCounterDetails(
        Rect area,
        EditorProfilerFrame frame,
        EditorProfilerModuleDefinition module)
    {
        var rows = module.Counters.Select(counter =>
            (counter.DisplayName, FormatCounterValue(
                CounterValue(frame, module.Id, counter.Name), counter.Unit))).ToArray();
        DrawCounterRows(area, rows);
    }

    private void DrawCounterRows(Rect area, (string Name, string Value)[] rows)
    {
        var contentHeight = Fix64.Max(area.height, 28 + rows.Length * 22);
        _detailsScroll = GUI.BeginScrollView(area, _detailsScroll,
            new Rect(0, 0, Fix64.Max(1, area.width - 11), contentHeight));
        try
        {
            var contentWidth = Fix64.Max(1, area.width - 11);
            var nameWidth = Fix64.Clamp(contentWidth * Fix64.FromDecimal(0.44m), 115, 310);
            GUI.Box(new Rect(0, 0, contentWidth, 24), GUIContent.none, EditorStyles.toolbar);
            GUI.Label(new Rect(8, 0, nameWidth - 8, 24), "Counter", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(nameWidth + 6, 0, contentWidth - nameWidth - 12, 24), "Value",
                EditorStyles.miniBoldLabel);
            for (var index = 0; index < rows.Length; index++)
            {
                var y = 24 + index * 22;
                GUI.Box(new Rect(0, y, contentWidth, 22), GUIContent.none,
                    index % 2 == 0 ? EditorStyles.treeViewRow : EditorStyles.viewBackground);
                GUI.Label(new Rect(9, y, nameWidth - 12, 22), rows[index].Name, EditorStyles.label);
                GUI.Label(new Rect(nameWidth + 6, y, contentWidth - nameWidth - 12, 22),
                    rows[index].Value, EditorStyles.label);
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private Fix64 HandleGraphSplitter(Rect splitter, Rect content, Fix64 graphHeight)
    {
        var id = GUIUtility.GetControlID("ProfilerGraphSplitter".GetHashCode(StringComparison.Ordinal),
            FocusType.Passive, splitter);
        var evt = Event.current;
        EditorGUIUtility.AddCursorRect(GUIUtility.hotControl == id
                ? new Rect(0, 0, GUIUtility.currentViewWidth, GUIUtility.currentViewHeight)
                : splitter,
            MouseCursor.ResizeVertical);
        var maximum = Fix64.Max(MinimumGraphHeight,
            content.height - MinimumDetailsHeight - SplitterHeight);
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown when evt.button == 0 && splitter.Contains(evt.mousePosition):
                GUIUtility.hotControl = id;
                _splitterDragStartY = evt.mousePosition.y;
                _splitterDragStartHeight = graphHeight;
                evt.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                graphHeight = Fix64.Clamp(_splitterDragStartHeight +
                    (evt.mousePosition.y - _splitterDragStartY),
                    Fix64.Min(MinimumGraphHeight, maximum), maximum);
                _graphHeightRatio = content.height <= 0 ? Fix64.Half : graphHeight / content.height;
                evt.Use();
                Repaint();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                evt.Use();
                break;
        }
        return graphHeight;
    }

    private ProfilerModuleEntry[] VisibleModules()
    {
        var all = AllModules();
        var current = all.Select(module => module.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var module in all)
            if (_knownModuleIds.Add(module.Id)) _visibleModuleIds.Add(module.Id);
        _knownModuleIds.RemoveWhere(id => !current.Contains(id));
        _visibleModuleIds.RemoveWhere(id => !current.Contains(id));
        return all.Where(module => _visibleModuleIds.Contains(module.Id)).ToArray();
    }

    private static ProfilerModuleEntry[] AllModules()
    {
        var modules = new List<ProfilerModuleEntry>
        {
            new(CpuModuleId, ModuleName(ProfilerModule.Cpu), ProfilerModule.Cpu, null),
            new(RenderingModuleId, ModuleName(ProfilerModule.Rendering), ProfilerModule.Rendering, null),
            new(MemoryModuleId, ModuleName(ProfilerModule.Memory), ProfilerModule.Memory, null)
        };
        modules.AddRange(EditorProfilerModuleRegistry.GetModules().Select(module =>
            new ProfilerModuleEntry($"external:{module.Id}", module.DisplayName, null, module)));
        return modules.ToArray();
    }

    private void ShowProfilerModulesMenu(Rect anchor)
    {
        _ = VisibleModules();
        var menu = new GenericMenu();
        foreach (var module in AllModules())
        {
            var captured = module;
            menu.AddItem(new GUIContent(captured.DisplayName),
                _visibleModuleIds.Contains(captured.Id), () => ToggleModuleVisibility(captured.Id));
        }
        menu.DropDown(anchor);
    }

    private void ToggleModuleVisibility(string moduleId)
    {
        if (_visibleModuleIds.Contains(moduleId))
        {
            if (_visibleModuleIds.Count <= 1) return;
            _visibleModuleIds.Remove(moduleId);
        }
        else _visibleModuleIds.Add(moduleId);

        if (!_visibleModuleIds.Contains(SelectedModuleId()))
        {
            var first = AllModules().FirstOrDefault(module => _visibleModuleIds.Contains(module.Id));
            if (first is not null) SelectModule(first);
        }
        _moduleTree?.InvalidateModules();
        Repaint();
    }

    private void SelectModule(ProfilerModuleEntry entry)
    {
        if (entry.BuiltIn is { } builtIn)
        {
            _module = builtIn;
            _externalModuleId = null;
        }
        else if (entry.External is { } external)
            _externalModuleId = external.Id;
        Repaint();
    }

    private string SelectedModuleId()
    {
        if (_externalModuleId is { } external) return $"external:{external}";
        return _module switch
        {
            ProfilerModule.Cpu => CpuModuleId,
            ProfilerModule.Rendering => RenderingModuleId,
            ProfilerModule.Memory => MemoryModuleId,
            _ => CpuModuleId
        };
    }

    private void HandleGraphClick(Rect plot, ProfilerModule module)
    {
        var evt = Event.current;
        if (evt.type != EventType.MouseDown || evt.button != 0 || !plot.Contains(evt.mousePosition)) return;
        var range = VisibleFrameRange(plot);
        if (range.Count <= 0) return;
        var normalized = Math.Clamp((double)((evt.mousePosition.x - plot.x) / plot.width), 0, 0.999999);
        var position = range.Start + Math.Clamp((int)(normalized * range.Count), 0, range.Count - 1);
        _selectedFrameIndex = _frames[position].FrameIndex;
        _followLatest = false;
        _module = module;
        _externalModuleId = null;
        evt.Use();
    }

    private void HandleExternalGraphClick(Rect plot, string moduleId)
    {
        var evt = Event.current;
        if (evt.type != EventType.MouseDown || evt.button != 0 || !plot.Contains(evt.mousePosition)) return;
        var range = VisibleFrameRange(plot);
        if (range.Count <= 0) return;
        var normalized = Math.Clamp((double)((evt.mousePosition.x - plot.x) / plot.width),
            0, 0.999999);
        var position = range.Start + Math.Clamp((int)(normalized * range.Count), 0, range.Count - 1);
        _selectedFrameIndex = _frames[position].FrameIndex;
        _followLatest = false;
        _externalModuleId = moduleId;
        evt.Use();
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (_clearOnPlay && state == PlayModeStateChange.EnteredPlayMode) ClearFrames();
    }

    private void OnProfilerModulesChanged()
    {
        if (_externalModuleId is not null &&
            EditorProfilerModuleRegistry.GetModules().All(module =>
                !string.Equals(module.Id, _externalModuleId, StringComparison.Ordinal)))
            _externalModuleId = null;
        _moduleTree?.InvalidateModules();
        _ = VisibleModules();
        Repaint();
    }

    private void ClearFrames()
    {
        EditorProfiler.Clear();
        _selectedFrameIndex = -1;
        _followLatest = true;
        RefreshSnapshot(force: true);
    }

    private void RefreshSnapshot(bool force)
    {
        var version = EditorProfiler.Version;
        if (!force && version == _observedVersion) return;
        var metadata = EditorProfiler.GetMetadata();
        if (!force && metadata.Version == _observedVersion) return;
        if (_frames.Length != metadata.Capacity) _frames = new EditorProfilerFrame[metadata.Capacity];
        _frameCount = EditorProfiler.CopyFrames(_frames, out metadata);
        if (_frames.Length != metadata.Capacity)
        {
            _frames = new EditorProfilerFrame[metadata.Capacity];
            _frameCount = EditorProfiler.CopyFrames(_frames, out metadata);
        }
        _observedVersion = metadata.Version;
        _snapshotRecording = metadata.Recording;
        _snapshotCapacity = metadata.Capacity;
        if (_followLatest) SelectLatest();
        else if (!ContainsFrame(_selectedFrameIndex))
        {
            _followLatest = true;
            SelectLatest();
        }
        Repaint();
    }

    private void SelectFirst()
    {
        if (_frameCount <= 0) return;
        _selectedFrameIndex = _frames[0].FrameIndex;
        _followLatest = false;
    }

    private void SelectRelative(int offset)
    {
        if (_frameCount <= 0) return;
        var position = SelectedFramePosition();
        position = Math.Clamp((position < 0 ? _frameCount - 1 : position) + offset,
            0, _frameCount - 1);
        _selectedFrameIndex = _frames[position].FrameIndex;
        _followLatest = position == _frameCount - 1;
    }

    private void SelectLatest() => _selectedFrameIndex = _frameCount > 0
        ? _frames[_frameCount - 1].FrameIndex
        : -1;

    private int SelectedFramePosition()
    {
        for (var index = _frameCount - 1; index >= 0; index--)
            if (_frames[index].FrameIndex == _selectedFrameIndex) return index;
        return -1;
    }

    private EditorProfilerFrame? SelectedFrame()
    {
        var position = SelectedFramePosition();
        return position >= 0 ? _frames[position] : null;
    }

    private EditorProfilerFrame? PreviousSelectedFrame()
    {
        var position = SelectedFramePosition();
        return position > 0 ? _frames[position - 1] : null;
    }

    private bool ContainsFrame(long frameIndex)
    {
        for (var index = _frameCount - 1; index >= 0; index--)
            if (_frames[index].FrameIndex == frameIndex) return true;
        return false;
    }

    private (int Start, int Count) VisibleFrameRange(Rect plot)
    {
        var maximumVisible = Math.Max(1, (int)(plot.width / 3));
        var count = Math.Min(_frameCount, maximumVisible);
        return (_frameCount - count, count);
    }

    private double Maximum(Func<EditorProfilerFrame, double> value)
    {
        var maximum = 0d;
        for (var index = 0; index < _frameCount; index++)
            maximum = Math.Max(maximum, value(_frames[index]));
        return maximum;
    }

    private static (string Name, string Value, Color Color)[] LegendEntries(
        ProfilerModule module, EditorProfilerFrame frame) => module switch
    {
        ProfilerModule.Cpu =>
        [
            ("Update", $"{frame.UpdateMilliseconds:F2}ms", CpuUpdateColor),
            ("Scripts", $"{frame.RuntimeMilliseconds:F2}ms", CpuScriptColor),
            ("Rendering", $"{frame.RenderMilliseconds:F2}ms", CpuRenderColor),
            ("IMGUI", $"{frame.ImGuiMilliseconds:F2}ms", CpuImGuiColor),
            ("Present", $"{frame.PresentMilliseconds:F2}ms", CpuPresentColor),
            ("Others", $"{OtherMilliseconds(frame):F2}ms", CpuOtherColor)
        ],
        ProfilerModule.Rendering =>
        [
            ("Batches", frame.RenderStatistics.BatchCount.ToString("N0"), RenderingBatchColor),
            ("Draw Calls", DrawValue(frame.RenderStatistics.DrawCallCount,
                frame.RenderStatistics.HasCompleteDrawStatistics), RenderingDrawColor),
            ("Triangles", DrawValue(frame.RenderStatistics.TriangleCount,
                frame.RenderStatistics.HasCompleteDrawStatistics), RenderingTriangleColor),
            ("Vertices", DrawValue(frame.RenderStatistics.VertexCount,
                frame.RenderStatistics.HasCompleteDrawStatistics), RenderingVertexColor)
        ],
        ProfilerModule.Memory =>
        [
            ("Heap", FormatBytes(frame.ManagedHeapBytes), MemoryHeapColor),
            ("Working", FormatBytes(frame.WorkingSetBytes), MemoryWorkingColor),
            ("Private", FormatBytes(frame.PrivateBytes), MemoryPrivateColor),
            ("GC Alloc", FormatBytes(frame.ManagedAllocatedBytes), MemoryAllocationColor)
        ],
        _ => []
    };

    private static (double Value, Color Color)[] CpuSamples(EditorProfilerFrame frame) =>
    [
        (frame.UpdateMilliseconds, CpuUpdateColor),
        (frame.RuntimeMilliseconds, CpuScriptColor),
        (frame.RenderMilliseconds, CpuRenderColor),
        (frame.ImGuiMilliseconds, CpuImGuiColor),
        (frame.PresentMilliseconds, CpuPresentColor),
        (OtherMilliseconds(frame), CpuOtherColor)
    ];

    private static ProfilerCounterDetail[] RenderingDetails(
        EditorProfilerFrame frame,
        EditorProfilerFrame? previous)
    {
        var statistics = frame.RenderStatistics;
        var savedByBatching = Math.Max(0,
            statistics.VisibleSubmissionCount - statistics.BatchCount);
        var batchEfficiency = statistics.VisibleSubmissionCount <= 0
            ? 0
            : savedByBatching * 100d / statistics.VisibleSubmissionCount;
        var averageVertices = statistics.DrawCallCount <= 0
            ? 0
            : statistics.VertexCount / (double)statistics.DrawCallCount;
        var averageTriangles = statistics.DrawCallCount <= 0
            ? 0
            : statistics.TriangleCount / (double)statistics.DrawCallCount;
        List<ProfilerCounterDetail> details =
        [
            IntegerDetail("Frame", "Frame Index", frame.FrameIndex,
                "Monotonic index assigned when this editor profiler frame completed."),
            MillisecondsDetail("Frame", "Frame Time", frame.FrameMilliseconds,
                "Total measured editor frame duration."),
            MillisecondsDetail("Frame", "Render Time", frame.RenderMilliseconds,
                "Time spent in the measured rendering phase."),
            PercentageDetail("Frame", "Render Share",
                frame.FrameMilliseconds <= 0 ? 0 : frame.RenderMilliseconds * 100d /
                frame.FrameMilliseconds,
                "Rendering time as a percentage of the total measured frame time."),
            TextDetail("Frame", "Domain",
                frame.RuntimeMilliseconds > 0 ? "Runtime + Editor" : "Editor",
                "Whether runtime work was observed in the captured editor frame."),
            IntegerDetail("Scene", "Cameras", statistics.CameraCount,
                "Number of cameras rendered across the scene render operations in this frame."),
            IntegerDetail("Scene", "Visible Submissions", statistics.VisibleSubmissionCount,
                "Visible renderer submissions before compatible submissions are batched."),
            IntegerDetail("Scene", "Batches", statistics.BatchCount,
                "Batches emitted by scene renderers after batching."),
            IntegerDetail("Scene", "Saved by Batching", savedByBatching,
                "Visible submissions merged into existing batches."),
            PercentageDetail("Scene", "Batching Efficiency", batchEfficiency,
                "Percentage of visible submissions saved by batching."),
            DecimalDetail("Scene", "Submissions / Camera",
                statistics.CameraCount <= 0 ? 0 : statistics.VisibleSubmissionCount /
                (double)statistics.CameraCount,
                "Average visible submissions processed per rendered camera."),
            DecimalDetail("Scene", "Batches / Camera",
                statistics.CameraCount <= 0 ? 0 : statistics.BatchCount /
                (double)statistics.CameraCount,
                "Average emitted batches per rendered camera."),
            IntegerDetail("Render Target", "Target Width", statistics.TargetWidth,
                "Width of the last scene render target in pixels."),
            IntegerDetail("Render Target", "Target Height", statistics.TargetHeight,
                "Height of the last scene render target in pixels."),
            IntegerDetail("Render Target", "Target Pixels",
                (long)statistics.TargetWidth * statistics.TargetHeight,
                "Pixel count of the last scene render target."),
            DecimalDetail("Render Target", "Aspect Ratio", statistics.TargetHeight <= 0
                    ? 0
                    : statistics.TargetWidth / (double)statistics.TargetHeight,
                "Width divided by height for the last scene render target."),
            BooleanDetail("Render Target", "Complete Draw Statistics",
                statistics.HasCompleteDrawStatistics,
                "True when every contributing graphics device supplied draw statistics.")
        ];
        if (previous is { } previousFrame)
            details.Insert(4, MillisecondsDetail("Frame", "Render Time Delta",
                frame.RenderMilliseconds - previousFrame.RenderMilliseconds,
                "Change in render time from the preceding retained profiler frame."));

        if (statistics.HasCompleteDrawStatistics)
        {
            details.AddRange(
            [
                IntegerDetail("Draw Calls", "Draw Calls", statistics.DrawCallCount,
                    "Draw calls reported by all contributing graphics devices."),
                IntegerDetail("Draw Calls", "Vertices", statistics.VertexCount,
                    "Submitted vertices across captured draw calls."),
                IntegerDetail("Draw Calls", "Triangles", statistics.TriangleCount,
                    "Submitted triangle primitives."),
                IntegerDetail("Draw Calls", "Lines", statistics.LineCount,
                    "Submitted line primitives."),
                DecimalDetail("Draw Calls", "Average Vertices / Draw", averageVertices,
                    "Submitted vertices divided by draw-call count."),
                DecimalDetail("Draw Calls", "Average Triangles / Draw", averageTriangles,
                    "Submitted triangles divided by draw-call count."),
                DecimalDetail("Draw Calls", "Average Lines / Draw",
                    statistics.DrawCallCount <= 0 ? 0 : statistics.LineCount /
                    (double)statistics.DrawCallCount,
                    "Submitted lines divided by draw-call count."),
                DecimalDetail("Draw Calls", "Vertices / Triangle",
                    statistics.TriangleCount <= 0 ? 0 : statistics.VertexCount /
                    (double)statistics.TriangleCount,
                    "Submitted vertices divided by submitted triangle count.")
            ]);
            if (previous is { } prior && prior.RenderStatistics.HasCompleteDrawStatistics)
                details.Add(IntegerDetail("Draw Calls", "Draw Call Delta",
                    statistics.DrawCallCount - prior.RenderStatistics.DrawCallCount,
                    "Change in draw-call count from the preceding retained profiler frame."));
        }
        else
            details.Add(TextDetail("Draw Calls", "Availability", "Unavailable",
                "At least one contributing graphics device did not expose complete draw statistics."));
        return details.ToArray();
    }

    private static ProfilerCounterDetail[] MemoryDetails(
        EditorProfilerFrame frame,
        EditorProfilerFrame? previous)
    {
        var liveManagedBytes = Math.Max(0, frame.ManagedHeapBytes - frame.ManagedFragmentedBytes);
        var details = new List<ProfilerCounterDetail>
        {
            IntegerDetail("Frame", "Frame Index", frame.FrameIndex,
                "Monotonic index assigned when this editor profiler frame completed."),
            MillisecondsDetail("Frame", "Frame Time", frame.FrameMilliseconds,
                "Total measured editor frame duration."),
            BytesDetail("Frame", "Allocated This Frame", frame.ManagedAllocatedBytes,
                "Managed bytes allocated by the current thread while this frame was recorded."),
            BytesDetail("Managed Memory", "Managed Heap", frame.ManagedHeapBytes,
                "Managed heap size reported by the runtime at frame completion."),
            BytesDetail("Managed Memory", "Estimated Live Managed", liveManagedBytes,
                "Managed heap minus runtime-reported fragmented bytes."),
            BytesDetail("Managed Memory", "Managed Fragmented", frame.ManagedFragmentedBytes,
                "Fragmented managed heap bytes reported by the runtime."),
            PercentageDetail("Managed Memory", "Fragmentation",
                frame.ManagedHeapBytes <= 0 ? 0 :
                frame.ManagedFragmentedBytes * 100d / frame.ManagedHeapBytes,
                "Runtime-reported fragmented bytes as a percentage of managed heap size."),
            PercentageDetail("Managed Memory", "Frame Allocations / Heap",
                frame.ManagedHeapBytes <= 0 ? 0 :
                frame.ManagedAllocatedBytes * 100d / frame.ManagedHeapBytes,
                "Bytes allocated during this frame as a percentage of managed heap size."),
            BytesDetail("Process Memory", "Process Working Set", frame.WorkingSetBytes,
                "Physical memory currently resident for the editor process."),
            BytesDetail("Process Memory", "Private Bytes", frame.PrivateBytes,
                "Memory committed exclusively to the editor process."),
            BytesDetail("Process Memory", "Private Minus Managed Heap",
                Math.Max(0, frame.PrivateBytes - frame.ManagedHeapBytes),
                "Private process memory excluding the measured managed heap; this also includes " +
                "native engine, graphics, runtime, and other process allocations."),
            IntegerDetail("Garbage Collection", "Generation 0 Collections",
                frame.Gen0Collections,
                "Cumulative generation 0 collection count reported by the runtime."),
            IntegerDetail("Garbage Collection", "Generation 1 Collections",
                frame.Gen1Collections,
                "Cumulative generation 1 collection count reported by the runtime."),
            IntegerDetail("Garbage Collection", "Generation 2 Collections",
                frame.Gen2Collections,
                "Cumulative generation 2 collection count reported by the runtime."),
            IntegerDetail("Garbage Collection", "Total Collections",
                (long)frame.Gen0Collections + frame.Gen1Collections + frame.Gen2Collections,
                "Sum of cumulative collection counts for all managed generations.")
        };
        if (frame.FrameMilliseconds > 0)
            details.Insert(3, BytesDetail("Frame", "Allocation Rate (Bytes / ms)",
                (long)Math.Round(frame.ManagedAllocatedBytes / frame.FrameMilliseconds),
                "Managed bytes allocated in this frame divided by measured frame duration."));
        if (previous is { } prior)
        {
            details.Add(BytesDetail("Managed Memory", "Heap Delta",
                frame.ManagedHeapBytes - prior.ManagedHeapBytes,
                "Change in managed heap size from the preceding retained profiler frame."));
            details.Add(BytesDetail("Process Memory", "Working Set Delta",
                frame.WorkingSetBytes - prior.WorkingSetBytes,
                "Change in process working set from the preceding retained profiler frame."));
            details.Add(IntegerDetail("Garbage Collection", "Generation 0 Delta",
                Math.Max(0, frame.Gen0Collections - prior.Gen0Collections),
                "Generation 0 collections since the preceding retained profiler frame."));
            details.Add(IntegerDetail("Garbage Collection", "Generation 1 Delta",
                Math.Max(0, frame.Gen1Collections - prior.Gen1Collections),
                "Generation 1 collections since the preceding retained profiler frame."));
            details.Add(IntegerDetail("Garbage Collection", "Generation 2 Delta",
                Math.Max(0, frame.Gen2Collections - prior.Gen2Collections),
                "Generation 2 collections since the preceding retained profiler frame."));
        }
        var allocations = frame.MethodSamples
            .Where(sample => sample.AllocatedBytes > 0)
            .OrderByDescending(sample => sample.AllocatedBytes)
            .Take(12)
            .ToArray();
        for (var index = 0; index < allocations.Length; index++)
        {
            var sample = allocations[index];
            var callStack = sample.CallStack.Length == 0
                ? "Call stack was not captured."
                : $"Call stack: {string.Join(" <- ", sample.CallStack.Take(6))}";
            details.Add(BytesDetail("Allocation Sites", $"#{index + 1} {sample.DisplayName}",
                sample.AllocatedBytes,
                $"{sample.Domain} domain, thread {sample.ThreadName} ({sample.ThreadId}), " +
                $"{sample.Calls:N0} call(s), self allocation {FormatBytes(sample.SelfAllocatedBytes)}. " +
                callStack));
        }
        if (allocations.Length == 0)
            details.Add(TextDetail("Allocation Sites", "Captured Sites", "None",
                "No captured method sample reported managed allocation for this frame."));
        return details.ToArray();
    }

    private static ProfilerCounterDetail IntegerDetail(
        string category,
        string name,
        long value,
        string description) => new(category, name, value.ToString("N0"), description,
        ProfilerCounterValueKind.Integer, IntegerValue: value);

    private static ProfilerCounterDetail BytesDetail(
        string category,
        string name,
        long value,
        string description) => new(category, name, FormatSignedBytes(value), description,
        ProfilerCounterValueKind.Bytes, IntegerValue: value);

    private static ProfilerCounterDetail MillisecondsDetail(
        string category,
        string name,
        double value,
        string description) => new(category, name, $"{value:F3} ms", description,
        ProfilerCounterValueKind.Milliseconds, NumericValue: value);

    private static ProfilerCounterDetail PercentageDetail(
        string category,
        string name,
        double value,
        string description) => new(category, name, $"{value:F1}%", description,
        ProfilerCounterValueKind.Percentage, NumericValue: value);

    private static ProfilerCounterDetail DecimalDetail(
        string category,
        string name,
        double value,
        string description) => new(category, name, value.ToString("N2"), description,
        ProfilerCounterValueKind.Decimal, NumericValue: value);

    private static ProfilerCounterDetail BooleanDetail(
        string category,
        string name,
        bool value,
        string description) => new(category, name, value ? "Yes" : "No", description,
        ProfilerCounterValueKind.Boolean, BooleanValue: value);

    private static ProfilerCounterDetail TextDetail(
        string category,
        string name,
        string value,
        string description) => new(category, name, value, description,
        ProfilerCounterValueKind.Text, TextValue: value);

    private static bool ToolbarButton(ref Fix64 x, Fix64 y, Fix64 width, string text)
    {
        var pressed = GUI.Button(new Rect(x, y, width, ToolbarRowHeight), text,
            EditorStyles.toolbarButton);
        x += width + 2;
        return pressed;
    }

    private static double OtherMilliseconds(EditorProfilerFrame frame) => Math.Max(0,
        frame.FrameMilliseconds - frame.UpdateMilliseconds - frame.RuntimeMilliseconds -
        frame.RenderMilliseconds - frame.ImGuiMilliseconds - frame.PresentMilliseconds);

    private string SelectedModuleName()
    {
        if (_externalModuleId is null) return ModuleName(_module);
        return EditorProfilerModuleRegistry.GetModules().FirstOrDefault(module =>
            string.Equals(module.Id, _externalModuleId, StringComparison.Ordinal))?.DisplayName ??
               "Profiler Module";
    }

    private static double CounterValue(
        EditorProfilerFrame frame,
        string moduleId,
        string counterName)
    {
        foreach (var sample in frame.CounterSamples)
            if (string.Equals(sample.ModuleId, moduleId, StringComparison.Ordinal) &&
                string.Equals(sample.CounterName, counterName, StringComparison.Ordinal))
                return sample.Value;
        return 0;
    }

    private static string FormatCounterValue(double value, EditorProfilerCounterUnit unit) =>
        unit switch
        {
            EditorProfilerCounterUnit.Milliseconds => $"{value:F3} ms",
            EditorProfilerCounterUnit.Bytes => FormatBytes((long)Math.Max(0, value)),
            EditorProfilerCounterUnit.Percentage => $"{value:F1}%",
            _ => value.ToString("N2")
        };

    private static string ModuleName(ProfilerModule module) => module switch
    {
        ProfilerModule.Cpu => "CPU Usage",
        ProfilerModule.Rendering => "Rendering",
        ProfilerModule.Memory => "Memory",
        _ => string.Empty
    };

    private static string DrawValue(long value, bool available) =>
        available ? value.ToString("N0") : "Unavailable";

    private static string FormatBytes(long bytes)
    {
        bytes = Math.Max(0, bytes);
        return bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
            _ => $"{bytes} B"
        };
    }

    private static string FormatSignedBytes(long bytes)
    {
        if (bytes >= 0) return FormatBytes(bytes);
        return $"-{FormatBytes(bytes == long.MinValue ? long.MaxValue : -bytes)}";
    }

    private static Color Rgb(int red, int green, int blue) => new(
        (Fix64)(red / 255d), (Fix64)(green / 255d), (Fix64)(blue / 255d), Fix64.One);

    private static void DrawEmptyState(Rect area, string message)
    {
        var height = Fix64.Max(22, EditorStyles.label.fixedHeight);
        GUI.Label(new Rect(area.x + 10, area.y + Fix64.Max(4, (area.height - height) / 2),
                Fix64.Max(1, area.width - 20), height), message, EditorStyles.centeredGreyMiniLabel);
    }

    private sealed record ProfilerModuleEntry(
        string Id,
        string DisplayName,
        ProfilerModule? BuiltIn,
        EditorProfilerModuleDefinition? External);

    private sealed class ProfilerModuleTreeView : TreeView<int>
    {
        private readonly Action<ProfilerModuleEntry> _selectionChanged;
        private readonly Action<Rect, ProfilerModuleEntry> _drawRow;
        private readonly Dictionary<int, ProfilerModuleEntry> _entriesById = [];
        private ProfilerModuleEntry[] _entries = [];
        private string _signature = string.Empty;

        internal ProfilerModuleTreeView(
            TreeViewState<int> state,
            Action<ProfilerModuleEntry> selectionChanged,
            Action<Rect, ProfilerModuleEntry> drawRow,
            int fixedRowHeight) : base(state)
        {
            _selectionChanged = selectionChanged;
            _drawRow = drawRow;
            rowHeight = fixedRowHeight;
            depthIndentWidth = 0;
            showAlternatingRowBackgrounds = false;
            showBorder = false;
            Reload();
        }

        internal void SetModules(IReadOnlyList<ProfilerModuleEntry> entries, string selectedId)
        {
            var signature = string.Join('\u001f', entries.Select(entry =>
                $"{entry.Id}\u001e{entry.DisplayName}"));
            if (!string.Equals(signature, _signature, StringComparison.Ordinal))
            {
                _entries = entries.ToArray();
                _signature = signature;
                Reload();
            }

            var selectedIndex = Array.FindIndex(_entries,
                entry => string.Equals(entry.Id, selectedId, StringComparison.Ordinal));
            var selection = GetSelection();
            if (selectedIndex < 0 || selection.Count == 1 && selection[0] == selectedIndex + 1)
                return;
            SetSelection([selectedIndex + 1], TreeViewSelectionOptions.RevealAndFrame);
        }

        internal void InvalidateModules() => _signature = string.Empty;

        protected override TreeViewItem<int> BuildRoot()
        {
            _entriesById.Clear();
            var root = new TreeViewItem<int>(0, -1, "Profiler Modules");
            for (var index = 0; index < _entries.Length; index++)
            {
                var id = index + 1;
                var entry = _entries[index];
                root.AddChild(new TreeViewItem<int>(id, 0, entry.DisplayName));
                _entriesById[id] = entry;
            }
            return root;
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;
        protected override bool CanRename(TreeViewItem<int> item) => false;

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count == 1 && _entriesById.TryGetValue(selectedIds[0], out var entry))
                _selectionChanged(entry);
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (_entriesById.TryGetValue(args.item.id, out var entry))
                _drawRow(args.rowRect, entry);
            else
                base.RowGUI(args);
        }
    }

    private enum ProfilerModule
    {
        Cpu,
        Rendering,
        Memory
    }

    private enum DetailsView
    {
        Hierarchy,
        RawHierarchy
    }
}
