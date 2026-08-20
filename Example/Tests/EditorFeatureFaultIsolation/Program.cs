using System.Reflection;
using System.Runtime.ExceptionServices;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using InspectorEditor = BEngine.Editor.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal static class Program
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo BeginFrame = RequireMethod(typeof(GUI), "BeginFrame", HiddenStatic);
    private static readonly MethodInfo EndFrame = RequireMethod(typeof(GUI), "EndFrame", HiddenStatic);
    private static readonly MethodInfo BeginWindow = RequireMethod(typeof(GUI), "BeginWindow", HiddenStatic);
    private static readonly MethodInfo OpenWindow = RequireMethod(typeof(EditorWindow), "OpenInternal", HiddenInstance);
    private static readonly MethodInfo CloseWindow = RequireMethod(typeof(EditorWindow), "CloseInternal", HiddenInstance);
    private static readonly MethodInfo FocusWindow = RequireMethod(typeof(EditorWindow), "FocusInternal", HiddenInstance);
    private static readonly MethodInfo UpdateWindow = RequireMethod(typeof(EditorWindow), "UpdateInternal", HiddenInstance);
    private static readonly MethodInfo DrawWindow = RequireMethod(typeof(EditorWindow), "OnGUIInternal", HiddenInstance);
    private static readonly MethodInfo NotifySelection = RequireMethod(typeof(EditorWindow),
        "NotifySelectionChanged", HiddenStatic);
    private static readonly MethodInfo NotifyHierarchy = RequireMethod(typeof(EditorWindow),
        "NotifyHierarchyChanged", HiddenStatic);
    private static readonly MethodInfo NotifyProject = RequireMethod(typeof(EditorWindow),
        "NotifyProjectChanged", HiddenStatic);
    private static readonly MethodInfo DrawInspector = RequireMethod(typeof(InspectorEditor),
        "OnInspectorGUIInternal", HiddenInstance);
    private static readonly Type MenuRegistryType = typeof(MenuItemAttribute).Assembly.GetType(
        "BEngine.Editor.MenuItemRegistry", throwOnError: true)!;
    private static readonly MethodInfo DiscoverMenus = RequireMethod(MenuRegistryType, "Discover",
        BindingFlags.Public | BindingFlags.Static);
    private static readonly MethodInfo ExecuteMenu = RequireMethod(MenuRegistryType, "Execute",
        BindingFlags.Public | BindingFlags.Instance);

    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            GUIUtility.pixelsPerPoint = Fix64.One;
            VerifyDebugSubscriberIsolation();

            var logs = new List<LogEntry>();
            BEngine.Debug.MessageLogged += Capture;
            try
            {
                VerifyWindowFaultIsolation(logs);
                VerifyExitGuiIsolation(logs);
                VerifyMenuFaultIsolation(logs);
                VerifyInspectorFaultIsolation(logs);
                VerifyPropertyDrawerFaultIsolation(logs);
                VerifyRepeatedFaultDeduplication(logs);
            }
            finally
            {
                BEngine.Debug.MessageLogged -= Capture;
            }

            Console.WriteLine("EDITOR_FEATURE_FAULT_ISOLATION_OK|debug-subscribers,window-gui-state," +
                              "window-lifecycle,exit-gui,menu,inspector,property-drawer,stack,dedup");
            return 0;

            void Capture(LogEntry entry) => logs.Add(entry);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_FEATURE_FAULT_ISOLATION_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyDebugSubscriberIsolation()
    {
        var firstCalls = 0;
        var lastCalls = 0;
        LogEntry lastEntry = default;
        Action<LogEntry> first = _ => firstCalls++;
        Action<LogEntry> broken = _ => throw new InvalidOperationException("DEBUG_SUBSCRIBER_SENTINEL");
        Action<LogEntry> last = entry =>
        {
            lastCalls++;
            lastEntry = entry;
        };
        BEngine.Debug.MessageLogged += first;
        BEngine.Debug.MessageLogged += broken;
        BEngine.Debug.MessageLogged += last;
        Exception? escaped = null;
        try { BEngine.Debug.LogError("DEBUG_SUBSCRIBER_PUBLISH"); }
        catch (Exception exception) { escaped = exception; }
        finally
        {
            BEngine.Debug.MessageLogged -= first;
            BEngine.Debug.MessageLogged -= broken;
            BEngine.Debug.MessageLogged -= last;
        }

        Require(escaped is null, "A throwing Debug.MessageLogged subscriber escaped Debug.LogError.");
        Require(firstCalls == 1 && lastCalls == 1,
            $"A throwing log subscriber stopped later subscribers: first={firstCalls}, last={lastCalls}.");
        Require(lastEntry.Message == "DEBUG_SUBSCRIBER_PUBLISH" &&
                lastEntry.StackTrace.Contains(nameof(VerifyDebugSubscriberIsolation), StringComparison.Ordinal),
            "Debug subscriber isolation lost the original log or its caller stack.");
    }

    private static void VerifyWindowFaultIsolation(List<LogEntry> logs)
    {
        ResetFaultCapture(logs);
        var faulting = new FaultingEditorWindow();
        var healthy = new HealthyEditorWindow();
        Invoke(OpenWindow, healthy);
        Invoke(OpenWindow, faulting);
        Require(faulting.EnableCalls == 1 && faulting.VisibleCalls == 1,
            "A failing OnEnable prevented the same window's OnBecameVisible callback.");
        Require(healthy.EnableCalls == 1 && healthy.VisibleCalls == 1,
            "The healthy window lifecycle did not complete its baseline setup.");

        Invoke(FocusWindow, faulting);
        Invoke(FocusWindow, healthy);
        Require(faulting.FocusCalls == 1 && faulting.LostFocusCalls == 1 && healthy.FocusCalls == 1,
            "A failing focus callback prevented focus from moving to a healthy window.");

        Invoke(NotifySelection, null);
        Invoke(NotifyHierarchy, null);
        Invoke(NotifyProject, null);
        Require(faulting.SelectionCalls == 1 && faulting.HierarchyCalls == 1 &&
                faulting.ProjectCalls == 1 && healthy.SelectionCalls == 1 &&
                healthy.HierarchyCalls == 1 && healthy.ProjectCalls == 1,
            "A failing window change callback stopped notifications for following windows.");

        Thread.Sleep(120);
        Invoke(UpdateWindow, faulting);
        Invoke(UpdateWindow, healthy);
        Require(faulting.UpdateCalls == 1 && faulting.InspectorUpdateCalls == 1,
            "A failing Update prevented the same window's OnInspectorUpdate callback.");
        Require(healthy.UpdateCalls == 1 && healthy.InspectorUpdateCalls == 1,
            "A failing window update stopped a healthy window's update callbacks.");

        var faultRect = new Rect(0, 0, 280, 220);
        var healthyRect = new Rect(320, 0, 300, 220);
        var commands = Render(new Event(EventType.Repaint), () =>
        {
            RenderWindow(faulting, faultRect);
            RenderWindow(healthy, healthyRect);
        });
        Require(healthy.GuiCalls == 1 && healthy.GuiWasEnabled,
            "A faulting window leaked disabled GUI state into a following window.");
        var label = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                               command.Content == "HEALTHY_WINDOW_LABEL");
        Require(label.Rect.X >= 320 && label.Rect.Right <= 620.1f &&
                label.ClipRect.X >= 319.9f && label.ClipRect.Right <= 620.1f,
            "An unbalanced group/scroll view in one window corrupted the next window's coordinates or clip.");

        var button = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                command.Content == "HEALTHY_WINDOW_BUTTON");
        var pointer = Center(button.Rect);
        Render(new Event(EventType.MouseDown) { mousePosition = pointer, button = 0 }, () =>
        {
            RenderWindow(faulting, faultRect);
            RenderWindow(healthy, healthyRect);
        });
        Render(new Event(EventType.MouseUp) { mousePosition = pointer, button = 0 }, () =>
        {
            RenderWindow(faulting, faultRect);
            RenderWindow(healthy, healthyRect);
        });
        Require(healthy.ButtonClicks == 1,
            "A faulting window corrupted control identity or input handling in the following window.");

        Invoke(CloseWindow, faulting);
        Invoke(CloseWindow, healthy);
        Require(faulting.InvisibleCalls == 1 && faulting.DisableCalls == 1,
            "A failing OnBecameInvisible prevented the same window's OnDisable callback.");
        Require(healthy.LostFocusCalls == 1 && healthy.InvisibleCalls == 1 && healthy.DisableCalls == 1,
            "A faulting window prevented a healthy window from completing its close lifecycle.");

        RequireFault(logs, "WINDOW_ENABLE_SENTINEL", "ThrowFromWindowEnable");
        RequireFault(logs, "WINDOW_UPDATE_SENTINEL", "ThrowFromWindowUpdate");
        RequireFault(logs, "WINDOW_GUI_SENTINEL", "ThrowFromWindowOnGui");
        RequireFault(logs, "WINDOW_DISABLE_SENTINEL", "ThrowFromWindowDisable");
    }

    private static void VerifyExitGuiIsolation(List<LogEntry> logs)
    {
        ResetFaultCapture(logs);
        var exit = new ExitGuiEditorWindow();
        var healthy = new HealthyEditorWindow();
        Invoke(OpenWindow, exit);
        Invoke(OpenWindow, healthy);
        var commands = Render(new Event(EventType.Repaint), () =>
        {
            RenderWindow(exit, new Rect(0, 0, 280, 180));
            RenderWindow(healthy, new Rect(320, 0, 300, 180));
        });
        Require(exit.GuiCalls == 1 && healthy.GuiCalls == 1 && healthy.GuiWasEnabled,
            "ExitGUI stopped a following EditorWindow or leaked its GUI state.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "HEALTHY_WINDOW_LABEL"),
            "A following window was not rendered after ExitGUI.");
        Require(!logs.Any(entry => entry.Message.Contains("ExitGUI", StringComparison.OrdinalIgnoreCase) ||
                                   entry.StackTrace.Contains(nameof(ExitGUIException),
                                       StringComparison.OrdinalIgnoreCase)),
            "ExitGUI was reported as an editor feature failure.");
        Invoke(CloseWindow, exit);
        Invoke(CloseWindow, healthy);
    }

    private static void VerifyMenuFaultIsolation(List<LogEntry> logs)
    {
        ResetFaultCapture(logs);
        FaultingMenuCommands.Reset();
        var registry = Invoke(DiscoverMenus, null) ??
                       throw new InvalidOperationException("MenuItemRegistry.Discover returned null.");
        Require(!Execute(registry, FaultingMenuCommands.BadCommandPath),
            "A failing MenuItem command reported success.");
        Require(Execute(registry, FaultingMenuCommands.HealthyCommandPath) &&
                FaultingMenuCommands.HealthyCommandCalls == 1,
            "A failing MenuItem command prevented a healthy command from executing.");
        Require(!Execute(registry, FaultingMenuCommands.BadValidatorPath) &&
                FaultingMenuCommands.ValidatedCommandCalls == 0,
            "A failing MenuItem validator enabled or executed its command.");
        Require(Execute(registry, FaultingMenuCommands.HealthyCommandPath) &&
                FaultingMenuCommands.HealthyCommandCalls == 2,
            "A failing MenuItem validator poisoned later menu execution.");
        RequireFault(logs, "MENU_COMMAND_SENTINEL", "ThrowFromBadMenuCommand");
        RequireFault(logs, "MENU_VALIDATOR_SENTINEL", "ThrowFromBadMenuValidator");
    }

    private static void VerifyInspectorFaultIsolation(List<LogEntry> logs)
    {
        ResetFaultCapture(logs);
        FaultingInspector.Reset();
        HealthyInspector.Reset();
        var target = ScriptableObject.CreateInstance<FaultProbeAsset>();
        using var faulting = InspectorEditor.CreateEditor(target, typeof(FaultingInspector));
        using var healthy = InspectorEditor.CreateEditor(target, typeof(HealthyInspector));
        Require(FaultingInspector.EnableCalls == 1 && HealthyInspector.EnableCalls == 1,
            "A failing custom Inspector OnEnable prevented a healthy Inspector from being created.");

        bool faultResult = true;
        bool healthyResult = false;
        var commands = Render(new Event(EventType.Repaint), () =>
        {
            faultResult = (bool)(Invoke(DrawInspector, faulting) ?? true);
            healthyResult = (bool)(Invoke(DrawInspector, healthy) ?? false);
        });
        Require(!faultResult && healthyResult && HealthyInspector.GuiCalls == 1 &&
                HealthyInspector.GuiWasEnabled,
            "A failing custom Inspector stopped or disabled the following Inspector.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "HEALTHY_INSPECTOR_LABEL"),
            "The healthy Inspector did not render after a failing custom Inspector.");

        faulting.Dispose();
        healthy.Dispose();
        Require(FaultingInspector.DisableCalls == 1 && HealthyInspector.DisableCalls == 1,
            "A failing custom Inspector OnDisable stopped a healthy Inspector lifecycle.");
        RequireFault(logs, "INSPECTOR_ENABLE_SENTINEL", "ThrowFromInspectorEnable");
        RequireFault(logs, "INSPECTOR_GUI_SENTINEL", "ThrowFromInspectorOnGui");
        RequireFault(logs, "INSPECTOR_DISABLE_SENTINEL", "ThrowFromInspectorDisable");
    }

    private static void VerifyPropertyDrawerFaultIsolation(List<LogEntry> logs)
    {
        ResetFaultCapture(logs);
        FaultingPropertyDrawer.Reset();
        _ = TypeCache.GetTypesDerivedFrom<PropertyDrawer>();
        var target = ScriptableObject.CreateInstance<FaultProbeAsset>();
        using var serialized = new SerializedObject(target);
        var broken = serialized.FindProperty(nameof(FaultProbeAsset.brokenValue)) ??
                     throw new InvalidOperationException("Faulting property was not serialized.");
        var healthy = serialized.FindProperty(nameof(FaultProbeAsset.healthyValue)) ??
                      throw new InvalidOperationException("Healthy property was not serialized.");
        var height = EditorGUI.GetPropertyHeight(broken);
        Require(height >= EditorGUIUtility.singleLineHeight && FaultingPropertyDrawer.HeightCalls == 1,
            "A failing PropertyDrawer.GetPropertyHeight did not return a readable fallback height.");

        var commands = Render(new Event(EventType.Repaint), () =>
        {
            EditorGUI.PropertyField(new Rect(0, 0, 420, 20), broken);
            Require(GUI.enabled, "A failing PropertyDrawer leaked disabled GUI state.");
            EditorGUI.PropertyField(new Rect(0, 28, 420, 20), healthy);
        });
        Require(FaultingPropertyDrawer.OnGuiCalls == 1,
            "The registered faulting PropertyDrawer was not invoked.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content.Contains("Healthy", StringComparison.OrdinalIgnoreCase)) &&
                commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == "23"),
            "A failing PropertyDrawer prevented the following default property from rendering.");
        RequireFault(logs, "PROPERTY_DRAWER_HEIGHT_SENTINEL", "ThrowFromPropertyDrawerHeight");
        RequireFault(logs, "PROPERTY_DRAWER_GUI_SENTINEL", "ThrowFromPropertyDrawerOnGui");
    }

    private static void VerifyRepeatedFaultDeduplication(List<LogEntry> logs)
    {
        ResetFaultCapture(logs);
        var faulting = new RepeatedFaultEditorWindow();
        var healthy = new HealthyEditorWindow();
        for (var index = 0; index < 8; index++)
        {
            Render(new Event(EventType.Repaint), () =>
            {
                RenderWindow(faulting, new Rect(0, 0, 280, 180));
                RenderWindow(healthy, new Rect(320, 0, 300, 180));
            });
        }
        var repeated = logs.Where(entry => entry.Type == LogType.Error &&
                                           entry.Message.Contains("REPEATED_WINDOW_GUI_SENTINEL",
                                               StringComparison.Ordinal)).ToArray();
        Require(repeated.Length > 0 && repeated.Length < 8,
            $"Repeated identical window faults were not rate limited: {repeated.Length}/8 logs.");
        Require(repeated[0].StackTrace.Contains("ThrowFromRepeatedWindowOnGui", StringComparison.Ordinal),
            "The retained repeated-fault log lost the original throw site.");
        Require(faulting.GuiCalls == 8 && healthy.GuiCalls == 8 && healthy.GuiWasEnabled,
            "Repeated failures eventually stopped or disabled a healthy EditorWindow.");
    }

    private static bool Execute(object registry, string path) =>
        (bool)(Invoke(ExecuteMenu, registry, path) ?? false);

    private static void ResetFaultCapture(List<LogEntry> logs)
    {
        logs.Clear();
        GUI.enabled = true;
        GUI.changed = false;
        GUIUtility.hotControl = 0;
        GUIUtility.keyboardControl = 0;
    }

    private static void RequireFault(IEnumerable<LogEntry> logs, string sentinel, string throwMethod)
    {
        var entry = logs.FirstOrDefault(candidate => candidate.Type == LogType.Error &&
                                                     candidate.Message.Contains(sentinel,
                                                         StringComparison.Ordinal));
        Require(!string.IsNullOrWhiteSpace(entry.Message), $"No error was logged for {sentinel}.");
        Require(entry.StackTrace.Contains(throwMethod, StringComparison.Ordinal),
            $"The {sentinel} log does not preserve its original throw site {throwMethod}.\n{entry.StackTrace}");
    }

    private static List<GpuCanvasCommand> Render(Event evt, Action draw)
    {
        var commands = new List<GpuCanvasCommand>();
        Invoke(BeginFrame, null, evt, 800, 400, commands);
        try { draw(); }
        finally { Invoke(EndFrame, null); }
        return commands;
    }

    private static void RenderWindow(EditorWindow window, Rect rect)
    {
        var boxedScope = Invoke(BeginWindow, null, rect) ??
                         throw new InvalidOperationException("GUI.BeginWindow returned no scope.");
        if (boxedScope is not IDisposable scope)
            throw new InvalidOperationException("GUI.BeginWindow scope is not disposable.");
        using (scope) Invoke(DrawWindow, window);
    }

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags) =>
        type.GetMethod(name, flags) ?? throw new MissingMethodException(type.FullName, name);

    private static object? Invoke(MethodInfo method, object? target, params object?[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
