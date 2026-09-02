using BEngine.Editor.Diagnostics;
using UnityEditor.IMGUI.Controls;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
internal sealed class FrameDebuggerWindow : EditorWindow
{
    private const int WideLayoutThreshold = 700;
    private const int WideToolbarThreshold = 620;
    private const int RowHeight = 22;
    private const int MinimumPreviewHeight = 48;
    private const int MinimumDetailsHeight = 100;
    private long _observedVersion = -1;
    private int _selectedEventIndex = -1;
    private readonly TreeViewState<int> _eventTreeState = new();
    private readonly SearchField _eventSearchField = new() { autoSetFocusOnFindCommand = true };
    private readonly FrameDebuggerDetailsView _detailsView = new();
    private readonly FrameDebuggerPreviewView _previewView = new();
    private FrameDebuggerEventTreeView? _eventTree;
    private bool _countedAsOpen;
    private static FrameDebuggerService Service => FrameDebuggerService.Shared;
    private static int _openWindowCount;

    [MenuItem("Window/Analysis/Frame Debugger", false, 210)]
    private static void Open() => GetWindow<FrameDebuggerWindow>("Frame Debugger");

    internal static void OpenForTarget(object target, string targetName)
    {
        ArgumentNullException.ThrowIfNull(target);
        var window = GetWindow<FrameDebuggerWindow>("Frame Debugger");
        Service.Enable(target, string.IsNullOrWhiteSpace(targetName) ? "Game" : targetName);
        Service.RequestCapture();
        window.Repaint();
    }

    protected override void OnEnable()
    {
        if (!_countedAsOpen)
        {
            _countedAsOpen = true;
            Interlocked.Increment(ref _openWindowCount);
        }
        if (_eventTree is null)
        {
            _eventTree = new FrameDebuggerEventTreeView(_eventTreeState, SelectTreeEvent);
            _eventSearchField.downOrUpArrowKeyPressed += _eventTree.SetFocusAndEnsureSelectedItem;
        }
        minSize = new Vector2(360, 300);
        titleContent = new GUIContent("Frame Debugger", EditorBuiltinIcons.Toolbar.FrameDebugger,
            "Inspect the render events captured for one frame");
    }

    protected override void OnDisable()
    {
        _previewView.Dispose();
        if (!_countedAsOpen) return;
        _countedAsOpen = false;
        if (Interlocked.Decrement(ref _openWindowCount) > 0) return;
        Interlocked.Exchange(ref _openWindowCount, 0);
        Service.Disable();
    }

    protected override void Update()
    {
        _ = Service.Preview;
        if (_observedVersion == Service.Version) return;
        _observedVersion = Service.Version;
        ClampSelection(Service.Snapshot);
        Repaint();
    }

    protected override void OnGUI()
    {
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight);
        var toolbarHeight = DrawToolbar(width);
        var content = new Rect(0, toolbarHeight, width, Fix64.Max(1, height - toolbarHeight));
        var snapshot = Service.Snapshot;
        if (snapshot is null)
        {
            DrawEmptyState(content, Service.Enabled
                ? "Waiting for the next rendered frame..."
                : "Enable Frame Debugger to capture render events.");
            return;
        }

        ClampSelection(snapshot);
        if (width >= WideLayoutThreshold)
        {
            var listWidth = Fix64.Clamp(width * Fix64.FromDecimal(0.34m), 280, 430);
            DrawEventList(new Rect(0, content.y, listWidth, content.height), snapshot);
            GUI.DrawRect(new Rect(listWidth, content.y, 1, content.height),
                EditorStyles.separator.normal.backgroundColor);
            DrawDetails(new Rect(listWidth + 1, content.y,
                Fix64.Max(1, width - listWidth - 1), content.height), snapshot);
            return;
        }

        var listHeight = Fix64.Clamp(content.height * Fix64.FromDecimal(0.38m), 100,
            Fix64.Max(100, content.height - 150));
        DrawEventList(new Rect(0, content.y, width, listHeight), snapshot);
        GUI.DrawRect(new Rect(0, content.y + listHeight, width, 1),
            EditorStyles.separator.normal.backgroundColor);
        DrawDetails(new Rect(0, content.y + listHeight + 1, width,
            Fix64.Max(1, content.height - listHeight - 1)), snapshot);
    }

    private Fix64 DrawToolbar(Fix64 width)
    {
        var rowHeight = Fix64.Max(22, EditorStyles.toolbar.fixedHeight);
        var wide = width >= WideToolbarThreshold;
        var height = wide ? rowHeight + 2 : rowHeight * 2 + 3;
        GUI.Box(new Rect(0, 0, width, height), GUIContent.none, EditorStyles.toolbar);

        var x = (Fix64)4;
        var y = (Fix64)1;
        if (ToolbarButton(ref x, y, 72, Service.Enabled ? "Disable" : "Enable",
                Service.Enabled ? EditorStyles.toolbarIconButtonSelected :
                EditorStyles.toolbarButton))
        {
            if (Service.Enabled) Service.Disable();
            else
            {
                Service.Enable();
                Service.RequestCapture();
            }
        }
        if (ToolbarButton(ref x, y, 66, "Capture", EditorStyles.toolbarButton))
            Service.RequestCapture();
        if (wide && ToolbarButton(ref x, y, 48, "Clear", EditorStyles.toolbarButton))
            Service.Clear();

        var snapshot = Service.Snapshot;
        var eventCount = snapshot?.Events.Count ?? 0;
        if (!wide)
        {
            x = 4;
            y += rowHeight + 1;
        }
        else x += 7;

        var targetWidth = wide ? (Fix64)106 : (Fix64)76;
        using (new EditorGUI.DisabledScope(true))
            _ = GUI.Button(new Rect(x, y, targetWidth, rowHeight),
                string.IsNullOrWhiteSpace(Service.TargetName) ? "Editor" : Service.TargetName,
                EditorStyles.toolbarPopup);
        x += targetWidth + 5;

        var labelWidth = wide ? (Fix64)98 : (Fix64)72;
        var displayedStep = Service.StepLimit < 0
            ? eventCount
            : Math.Clamp(Service.StepLimit, 0, eventCount);
        GUI.Label(new Rect(x, y, labelWidth, rowHeight), eventCount == 0
            ? "Events: 0"
            : $"Event: {displayedStep}/{eventCount}", EditorStyles.toolbarLabel);
        x += labelWidth + 3;

        var navigationWidth = (Fix64)50;
        var remaining = Fix64.Max(1, width - x - navigationWidth - 8);
        if (eventCount > 0 && remaining >= 34)
        {
            var value = GUI.HorizontalSlider(
                new Rect(x, y + (rowHeight - 16) / 2, remaining, 16),
                Math.Max(1, displayedStep), 1, eventCount);
            var step = Math.Clamp((int)Math.Round((double)value), 1, eventCount);
            if (step != displayedStep)
            {
                SelectEventIndex(step - 1);
            }
        }
        x += remaining + 3;
        using (new EditorGUI.DisabledScope(eventCount == 0 || _selectedEventIndex <= 0))
            if (GUI.Button(new Rect(x, y, 23, rowHeight), "<", EditorStyles.toolbarButtonLeft))
                SelectEventIndex(_selectedEventIndex - 1);
        x += 23;
        using (new EditorGUI.DisabledScope(eventCount == 0 ||
                                            _selectedEventIndex >= eventCount - 1))
            if (GUI.Button(new Rect(x, y, 23, rowHeight), ">", EditorStyles.toolbarButtonRight))
                SelectEventIndex(_selectedEventIndex + 1);
        return height;
    }

    private static bool ToolbarButton(ref Fix64 x, Fix64 y, Fix64 width, string text, GUIStyle style)
    {
        var height = Fix64.Max(22, EditorStyles.toolbar.fixedHeight);
        var pressed = GUI.Button(new Rect(x, y, width, height), text, style);
        x += width + 2;
        return pressed;
    }

    private void DrawEventList(Rect area, FrameDebugCaptureSnapshot snapshot)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var header = new Rect(area.x + 5, area.y + 2, Fix64.Max(1, area.width - 10), RowHeight);
        GUI.Label(header, new GUIContent(
                $"{snapshot.TargetName}  |  {snapshot.Backend}  |  " +
                $"Capture {snapshot.CaptureId}  |  {snapshot.Events.Count:N0} events",
                $"Captured {snapshot.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}"),
            EditorStyles.miniBoldLabel);
        var searchRect = new Rect(area.x + 3, header.yMax + 1, Fix64.Max(1, area.width - 6),
            RowHeight);
        _eventTree ??= new FrameDebuggerEventTreeView(_eventTreeState, SelectTreeEvent);
        var search = _eventSearchField.OnToolbarGUI(searchRect, _eventTree.searchString);
        if (!string.Equals(search, _eventTree.searchString, StringComparison.Ordinal))
            _eventTree.searchString = search;
        var treeRect = new Rect(area.x + 1, searchRect.yMax + 1, Fix64.Max(1, area.width - 2),
            Fix64.Max(1, area.yMax - searchRect.yMax - 2));
        if (snapshot.Events.Count == 0)
        {
            GUI.Label(treeRect, "No render events were captured.", EditorStyles.centeredGreyMiniLabel);
            return;
        }
        _eventTree.SetSnapshot(snapshot, _selectedEventIndex);
        _eventTree.OnGUI(treeRect);
    }

    private void SelectTreeEvent(int snapshotIndex)
    {
        SelectEventIndex(snapshotIndex);
    }

    private void DrawDetails(Rect area, FrameDebugCaptureSnapshot snapshot)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        if (_selectedEventIndex < 0 || _selectedEventIndex >= snapshot.Events.Count)
        {
            DrawEmptyState(area, "Select a render event to inspect its state.");
            return;
        }

        var item = snapshot.Events[_selectedEventIndex];
        var availableHeight = Fix64.Max(1, area.height - 1);
        var maximumPreviewHeight = Fix64.Max(1, availableHeight - MinimumDetailsHeight);
        var minimumPreviewHeight = Fix64.Min(MinimumPreviewHeight, maximumPreviewHeight);
        var previewHeight = Fix64.Clamp(
            area.height * Fix64.FromDecimal(0.43m),
            minimumPreviewHeight,
            maximumPreviewHeight);
        var previewArea = new Rect(area.x, area.y, area.width, previewHeight);
        _previewView.Draw(previewArea, Service.Preview);
        GUI.DrawRect(new Rect(area.x, previewArea.yMax, area.width, 1),
            EditorStyles.separator.normal.backgroundColor);
        var detailsArea = new Rect(area.x, previewArea.yMax + 1, area.width,
            Fix64.Max(1, area.yMax - previewArea.yMax - 1));
        _detailsView.Draw(detailsArea, snapshot, item,
            IsExecutedAtCurrentStep(item, Service.StepLimit), Service.Preview);
    }

    private void SelectEventIndex(int snapshotIndex)
    {
        var snapshot = Service.Snapshot;
        if (snapshot is null || (uint)snapshotIndex >= (uint)snapshot.Events.Count) return;
        _selectedEventIndex = snapshotIndex;
        _detailsView.ResetScroll();
        Service.SetStepLimit(snapshot.Events[snapshotIndex].Index + 1);
        Repaint();
    }

    private static void DrawEmptyState(Rect area, string message)
    {
        var height = Fix64.Max(22, EditorStyles.label.fixedHeight);
        GUI.Label(new Rect(area.x + 10, area.y + Fix64.Max(4, (area.height - height) / 2),
                Fix64.Max(1, area.width - 20), height), message, EditorStyles.centeredGreyMiniLabel);
    }

    private static bool IsExecutedAtCurrentStep(FrameDebugEvent item, int stepLimit) =>
        item.Executed && (stepLimit < 0 || item.Index < stepLimit);

    private void ClampSelection(FrameDebugCaptureSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Events.Count == 0)
        {
            _selectedEventIndex = -1;
            return;
        }

        var stepLimit = Service.StepLimit;
        _selectedEventIndex = stepLimit < 0
            ? snapshot.Events.Count - 1
            : Math.Clamp(Math.Max(0, stepLimit - 1), 0, snapshot.Events.Count - 1);
    }
}
