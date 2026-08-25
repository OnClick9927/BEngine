using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal sealed class EditorKeyboardHarness : IDisposable
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                  BindingFlags.NonPublic;
    private readonly Type _applicationType;
    private readonly object _nativeWindow;
    private readonly object _inspectorWindow;
    private readonly object[] _windows;
    private readonly ProjectAssetDatabase _assets;
    private readonly BPackageManager _packages;
    private bool _disposed;

    internal object Application { get; }
    internal object HierarchyWindow { get; }
    internal object ProjectWindow { get; }
    internal Scene Scene { get; }

    internal EditorKeyboardHarness(KeyboardCommandFixture fixture)
    {
        var editorAssembly = typeof(EditorWindow).Assembly;
        _applicationType = TestAssert.RequireType(editorAssembly, "BEngine.Editor.GpuEditorApplication");
        Application = RuntimeHelpers.GetUninitializedObject(_applicationType);
        _assets = new ProjectAssetDatabase(fixture.Workspace);
        _assets.Refresh();
        _packages = (BPackageManager)(Activator.CreateInstance(typeof(BPackageManager), InstanceMembers,
            binder: null, [fixture.Workspace, null, false], culture: null) ??
                                      throw new InvalidOperationException("Could not create the Package Manager."));
        Scene = Document.LoadBObject<SceneDocument, Scene>(fixture.ScenePath, new EmptyServiceProvider());
        SetProperty(Scene, "path", fixture.ScenePath);

        var openSceneType = TestAssert.RequireType(editorAssembly, "BEngine.Editor.EditorOpenScene");
        var entry = Activator.CreateInstance(openSceneType, InstanceMembers, binder: null,
            [Scene, fixture.ScenePath, ToAssetPath(fixture, fixture.ScenePath), true], culture: null) ??
                    throw new InvalidOperationException("Could not construct the open Scene entry.");
        var openScenes = (IList)(Activator.CreateInstance(typeof(List<>).MakeGenericType(openSceneType)) ??
                                 throw new InvalidOperationException("Could not create the open Scene list."));
        openScenes.Add(entry);

        var dock = Create(editorAssembly, "BEngine.Editor.ImGuiDockWorkspace");
        _nativeWindow = Create(editorAssembly, "BEngine.Editor.ImGuiNativeWindow",
            "Keyboard Commands Test", 900, 640, false);
        HierarchyWindow = CreateNested("ImGuiHierarchyWindow", Application);
        ProjectWindow = CreateNested("ImGuiProjectWindow", Application);
        var sceneWindow = CreateNested("ImGuiSceneWindow", Application);
        var gameWindow = CreateNested("ImGuiGameWindow", Application);
        _inspectorWindow = CreateNested("ImGuiInspectorWindow", Application);
        _windows = [HierarchyWindow, ProjectWindow, sceneWindow, gameWindow, _inspectorWindow];

        SetField("_workspace", fixture.Workspace);
        SetField("_services", new EmptyServiceProvider());
        SetField("_assets", _assets);
        SetField("_packages", _packages);
        SetField("_scene", Scene);
        SetField("_scenePath", fixture.ScenePath);
        SetField("_openScenes", openScenes);
        SetField("_loadedSceneSnapshot", new[] { Scene });
        SetField("_dock", dock);
        SetField("_mainWindow", _nativeWindow);
        SetField("_hierarchy", HierarchyWindow);
        SetField("_sceneView", sceneWindow);
        SetField("_gameView", gameWindow);
        SetField("_project", ProjectWindow);
        SetField("_inspector", _inspectorWindow);
        InitializeField("_editorPanels");
        InitializeField("_windowLayer");
        InitializeField("_builtInWindows");
        InitializeField("_runtimes");
        AddBuiltIn(HierarchyWindow, "Left", true);
        AddBuiltIn(sceneWindow, "Center", true);
        AddBuiltIn(gameWindow, "Center", true);
        AddBuiltIn(_inspectorWindow, "Right", true);
        AddBuiltIn(ProjectWindow, "Bottom", true);
        AttachBridge(editorAssembly);
    }

    internal GameObject Root => Scene.Find("Keyboard Root") ??
                                throw new InvalidOperationException("The keyboard test root no longer exists.");

    internal void Select(GameObject gameObject)
    {
        ResetTextFocus();
        var select = _applicationType.GetMethod("Select", InstanceMembers, binder: null,
            [typeof(GameObject)], modifiers: null) ??
                     throw new MissingMethodException(_applicationType.FullName, "Select(GameObject)");
        select.Invoke(Application, [gameObject]);
    }

    internal void SelectProject(string virtualPath)
    {
        ResetTextFocus();
        SetField(ProjectWindow, "_selectedPath", virtualPath.Replace('\\', '/'));
    }

    internal void RefreshProject()
    {
        _assets.Refresh();
        Invoke(ProjectWindow, "Invalidate");
    }

    internal Event SendHierarchyKey(KeyCode key, EventModifiers modifiers = EventModifiers.None,
        char character = '\0')
    {
        Focus(HierarchyWindow);
        var evt = Key(key, modifiers, character);
        Render(HierarchyWindow, evt);
        return evt;
    }

    internal Event SendProjectKey(KeyCode key, EventModifiers modifiers = EventModifiers.None,
        char character = '\0')
    {
        Focus(ProjectWindow);
        var evt = Key(key, modifiers, character);
        Render(ProjectWindow, evt);
        return evt;
    }

    internal IReadOnlyList<GpuCanvasCommand> RenderHierarchy(Event evt) => Render(HierarchyWindow, evt);
    internal IReadOnlyList<GpuCanvasCommand> RenderProject(Event evt) => Render(ProjectWindow, evt);

    internal Guid? HierarchyRenamingId => GetField(HierarchyWindow, "_renamingId") as Guid?;
    internal string HierarchyRenameValue => GetField(HierarchyWindow, "_renameValue") as string ?? string.Empty;
    internal string? ProjectRenamingPath => GetField(ProjectWindow, "_renamingPath") as string;
    internal string ProjectRenameValue => GetField(ProjectWindow, "_renameValue") as string ?? string.Empty;
    internal GameObject? SelectedGameObject => GetField(Application, "_selected") as GameObject;

    internal void SetHierarchyRenameValue(string value) => SetField(HierarchyWindow, "_renameValue", value);
    internal void SetProjectRenameValue(string value) => SetField(ProjectWindow, "_renameValue", value);

    internal void FocusHierarchyRenameField(string value)
    {
        var commands = RenderHierarchy(new Event(EventType.Repaint));
        ClickText(HierarchyWindow, commands, value);
    }

    internal void FocusProjectRenameField(string value)
    {
        var commands = RenderProject(new Event(EventType.Repaint));
        ClickText(ProjectWindow, commands, value);
    }

    internal static int CountFiles(string directory) => Directory.EnumerateFiles(directory)
        .Count(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachBridge(typeof(EditorWindow).Assembly);
        foreach (var window in _windows)
        {
            try { Invoke(window, "CloseInternal"); }
            catch (TargetInvocationException) { }
        }
        if (Scene.isCreated) Scene.Dispose();
        _packages.Dispose();
        try { ((IDisposable)_nativeWindow).Dispose(); }
        catch (InvalidOperationException) { }
        ResetTextFocus();
        Undo.ClearAll();
    }

    private IReadOnlyList<GpuCanvasCommand> Render(object window, Event evt)
    {
        var commands = new List<GpuCanvasCommand>();
        var beginFrame = typeof(GUI).GetMethod("BeginFrame", BindingFlags.Static | BindingFlags.NonPublic) ??
                         throw new MissingMethodException(typeof(GUI).FullName, "BeginFrame");
        var endFrame = typeof(GUI).GetMethod("EndFrame", BindingFlags.Static | BindingFlags.NonPublic) ??
                       throw new MissingMethodException(typeof(GUI).FullName, "EndFrame");
        beginFrame.Invoke(null, [evt, 720, 620, commands]);
        try
        {
            Invoke(window, "OnGUIInternal");
            if (evt.type != EventType.Used) Invoke(Application, "HandleGlobalKeyboard");
        }
        finally { endFrame.Invoke(null, null); }
        return commands;
    }

    private void ClickText(object window, IEnumerable<GpuCanvasCommand> commands, string value)
    {
        var command = commands.LastOrDefault(item => item.Type == GpuCanvasCommandType.Text &&
                                                     item.Content == value);
        TestAssert.Require(command.Type == GpuCanvasCommandType.Text,
            $"Could not find the active rename text field containing '{value}'.");
        var point = new Vector2((Fix64)(command.Rect.X + command.Rect.Width / 2),
            (Fix64)(command.Rect.Y + command.Rect.Height / 2));
        Render(window, new Event(EventType.MouseDown) { mousePosition = point, button = 0, clickCount = 1 });
        TestAssert.Require(GUIUtility.keyboardControl != 0 && GUIUtility.textFieldInput,
            "Clicking the rename field did not establish text-input focus.");
    }

    private static Event Key(KeyCode key, EventModifiers modifiers, char character) =>
        new(EventType.KeyDown) { keyCode = key, modifiers = modifiers, character = character };

    private void Focus(object window) => Invoke(window, "FocusInternal");

    private void AddBuiltIn(object window, string area, bool select)
    {
        var method = _applicationType.GetMethod("AddBuiltIn", InstanceMembers) ??
                     throw new MissingMethodException(_applicationType.FullName, "AddBuiltIn");
        method.Invoke(Application, [window, Enum.Parse(method.GetParameters()[1].ParameterType, area), select]);
    }

    private object CreateNested(string name, params object[] arguments)
    {
        var type = _applicationType.GetNestedType(name, BindingFlags.NonPublic) ??
                   throw new TypeLoadException($"{_applicationType.FullName}+{name}");
        return Activator.CreateInstance(type, InstanceMembers, binder: null, arguments, culture: null) ??
               throw new InvalidOperationException($"Could not create {type.FullName}.");
    }

    private static object Create(Assembly assembly, string fullName, params object[] arguments)
    {
        var type = TestAssert.RequireType(assembly, fullName);
        return Activator.CreateInstance(type, InstanceMembers, binder: null, arguments, culture: null) ??
               throw new InvalidOperationException($"Could not create {fullName}.");
    }

    private void InitializeField(string name)
    {
        var field = RequireField(_applicationType, name);
        SetField(name, Activator.CreateInstance(field.FieldType) ??
                       throw new InvalidOperationException($"Could not initialize {name}."));
    }

    private void SetField(string name, object? value) => SetField(Application, name, value);
    private static void SetField(object target, string name, object? value) =>
        RequireField(target.GetType(), name).SetValue(target, value);
    private static object? GetField(object target, string name) => RequireField(target.GetType(), name).GetValue(target);
    private static FieldInfo RequireField(Type type, string name) => type.GetField(name, InstanceMembers) ??
        throw new MissingFieldException(type.FullName, name);

    private static void SetProperty(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name, InstanceMembers) ??
                       throw new MissingMemberException(target.GetType().FullName, name);
        property.SetValue(target, value);
    }

    private static object? Invoke(object target, string name)
    {
        MethodInfo? method = null;
        for (var type = target.GetType(); type is not null && method is null; type = type.BaseType)
            method = type.GetMethod(name, InstanceMembers | BindingFlags.DeclaredOnly);
        if (method is null) throw new MissingMethodException(target.GetType().FullName, name);
        return method.Invoke(target, null);
    }

    private static string ToAssetPath(KeyboardCommandFixture fixture, string sourcePath) =>
        Path.GetRelativePath(fixture.Workspace.RootPath, sourcePath).Replace('\\', '/');

    private void AttachBridge(Assembly assembly)
    {
        var bridge = TestAssert.RequireType(assembly, "BEngine.Editor.EditorBridge");
        bridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Application]);
    }

    private void DetachBridge(Assembly assembly)
    {
        var bridge = TestAssert.RequireType(assembly, "BEngine.Editor.EditorBridge");
        bridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Application]);
    }

    private static void ResetTextFocus()
    {
        GUIUtility.keyboardControl = 0;
        typeof(GUIUtility).GetProperty("textFieldInput", BindingFlags.Static | BindingFlags.Public)!
            .SetValue(null, false);
    }
}
