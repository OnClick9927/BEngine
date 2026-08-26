using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.PackageManagerLifecycle;

internal static class PackageManagerViewSmoke
{
    private const int NarrowPanelWidth = 640;
    private const int NarrowPanelHeight = 720;
    private const float GeometryTolerance = 0.5f;
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod(
        "BeginFrame", BindingFlags.Static | BindingFlags.NonPublic) ??
        throw new MissingMethodException(typeof(GUI).FullName, "BeginFrame");
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod(
        "EndFrame", BindingFlags.Static | BindingFlags.NonPublic) ??
        throw new MissingMethodException(typeof(GUI).FullName, "EndFrame");
    private static readonly MethodInfo DrawWindow = typeof(EditorWindow).GetMethod(
        "OnGUIInternal", HiddenInstance) ??
        throw new MissingMethodException(typeof(EditorWindow).FullName, "OnGUIInternal");
    private static readonly MethodInfo CloseWindow = typeof(EditorWindow).GetMethod(
        "CloseInternal", HiddenInstance) ??
        throw new MissingMethodException(typeof(EditorWindow).FullName, "CloseInternal");

    internal static void Verify(ProjectWorkspace workspace, BPackageManager manager)
    {
        var editor = typeof(EditorWindow).Assembly;
        var applicationType = editor.GetType("BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        var window = editor.GetTypes().Single(type => type.Name == "ImGuiPackageManagerWindow");
        var onGui = window.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic);
        if (onGui is null || onGui.DeclaringType != window)
            throw new InvalidOperationException("Package Manager does not provide its own IMGUI OnGUI implementation.");
        if (window.GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType != typeof(string))
            throw new InvalidOperationException("Package Manager is missing its IMGUI search state.");
        if (window.GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic) is null)
            throw new InvalidOperationException("Package Manager is missing its selected-package detail state.");
        var tabType = window.GetNestedType("PackageDetailTab", BindingFlags.NonPublic) ??
                      throw new InvalidOperationException("Package Manager is missing its detail-tab model.");
        var expectedTabs = new[] { "Description", "Dependencies", "Examples" };
        if (!tabType.IsEnum || !Enum.GetNames(tabType).SequenceEqual(expectedTabs, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Package Manager detail tabs must be Description, Dependencies and Examples.");
        var selectedTab = window.GetField("_selectedTab", HiddenInstance);
        if (selectedTab?.FieldType != tabType)
            throw new InvalidOperationException("Package Manager is missing its selected detail-tab state.");
        if (window.GetField("_coreSelected", BindingFlags.Instance | BindingFlags.NonPublic) is null ||
            window.GetMethod("DrawDescription", Hidden) is null ||
            window.GetMethod("DrawDependencies", HiddenInstance) is null ||
            window.GetMethod("DrawExamples", BindingFlags.Instance | BindingFlags.NonPublic) is null ||
            window.GetMethod("ImportExample", BindingFlags.Instance | BindingFlags.NonPublic) is null)
            throw new InvalidOperationException(
                "Package Manager must present description, dependency and example pages in its detail view.");
        var drawExamples = window.GetMethod("DrawExamples", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var drawParameters = drawExamples.GetParameters();
        if (drawParameters.Length != 2 || drawParameters[0].ParameterType != typeof(string) ||
            drawParameters[1].ParameterType != typeof(bool))
            throw new InvalidOperationException(
                "Package examples must be gated only by the selected package's enabled state.");
        var importExample = window.GetMethod("ImportExample", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (importExample.GetParameters().Length != 2)
            throw new InvalidOperationException(
                "Importing an example must not implicitly enable or install its owning package.");
        if (typeof(GUIContent).Assembly.GetType("BEngine.UIElements.VisualElement", false) is not null)
            throw new InvalidOperationException("Core package UI must not depend on UIElements.");

        EditorAppearance.Apply(new EditorPreferencesDocument());
        var application = RuntimeHelpers.GetUninitializedObject(applicationType);
        SetField(applicationType, application, "_workspace", workspace);
        SetField(applicationType, application, "_packages", manager);
        var projectType = applicationType.GetNestedType("ImGuiProjectWindow", BindingFlags.NonPublic) ??
                          throw new TypeLoadException("GpuEditorApplication.ImGuiProjectWindow");
        var projectConstructor = projectType.GetConstructor(BindingFlags.Instance | BindingFlags.Public |
                                                            BindingFlags.NonPublic, null, [applicationType], null) ??
                                 throw new MissingMethodException(projectType.FullName,
                                     ".ctor(GpuEditorApplication)");
        SetField(applicationType, application, "_project", projectConstructor.Invoke([application]));
        var constructor = window.GetConstructor(BindingFlags.Instance | BindingFlags.Public |
                                                BindingFlags.NonPublic, null, [applicationType], null) ??
                          throw new MissingMethodException(window.FullName, ".ctor(GpuEditorApplication)");
        var view = (EditorWindow)(constructor.Invoke([application]) ??
                                  throw new InvalidOperationException("Could not create Package Manager."));
        var editorBridge = editor.GetType("BEngine.Editor.EditorBridge", throwOnError: true)!;
        var attachHost = editorBridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic) ??
                         throw new MissingMethodException(editorBridge.FullName, "Attach");
        var detachHost = editorBridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic) ??
                         throw new MissingMethodException(editorBridge.FullName, "Detach");
        try
        {
            SetField(window, view, "_coreSelected", true);
            SetField(window, view, "_selected", null);
            SetField(window, view, "_selectedPackageId", null);
            SetTab(selectedTab, view, "Description");
            var coreText = DetailText(Render(view));
            Require(coreText.Count(text => text.Equals("Documentation", StringComparison.Ordinal)) == 1,
                "BEngine Core does not expose its Documentation button.");
            var documentationCatalog = editor.GetType(
                "BEngine.Editor.PackageDocumentationCatalog", throwOnError: true)!;
            var coreDocumentation = documentationCatalog.GetMethod(
                "FindCoreDocumentation", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .Invoke(null, null) as string;
            Require(coreDocumentation is not null && File.Exists(coreDocumentation),
                "BEngine Core offline documentation is missing.");
            var exampleCatalog = editor.GetType(
                "BEngine.Editor.PackageExampleCatalog", throwOnError: true)!;
            var coreExamplesDirectory = exampleCatalog.GetMethod(
                    "FindCoreExamplesDirectory", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .Invoke(null, null) as string;
            var coreArchives = FindExampleArchives(coreExamplesDirectory);
            Require(coreArchives.Length > 0, "BEngine Core has no examples to present.");
            VerifyExampleView(view, window, selectedTab, "BEngine Core", coreArchives, 0,
                applicationType, application, workspace, attachHost, detachHost);
            foreach (var package in manager.definitions)
                VerifyPackageView(view, window, selectedTab, package, manager, applicationType, application,
                    workspace, attachHost, detachHost);
        }
        finally
        {
            try { CloseWindow.Invoke(view, null); }
            catch (TargetInvocationException) { }
        }
    }

    private static void VerifyPackageView(
        EditorWindow view,
        Type windowType,
        FieldInfo selectedTab,
        BPackageDefinition package,
        BPackageManager manager,
        Type applicationType,
        object application,
        ProjectWorkspace workspace,
        MethodInfo attachHost,
        MethodInfo detachHost)
    {
        SetField(windowType, view, "_selected", package);
        SetField(windowType, view, "_selectedPackageId", package.Document.Id);
        SetField(windowType, view, "_coreSelected", false);
        SetTab(selectedTab, view, "Description");
        var description = DetailText(Render(view));
        RequireTabs(description, package.Document.DisplayName);
        var expectedDescription = string.IsNullOrWhiteSpace(package.Document.Description)
            ? "No description available."
            : package.Document.Description;
        Require(description.Contains(expectedDescription, StringComparer.Ordinal),
            $"{package.Document.DisplayName} Description tab did not display its description.");
        Require(description.Count(text => text.Equals("Import", StringComparison.Ordinal)) == 1,
            $"{package.Document.DisplayName} does not have exactly one package Import button.");
        Require(description.Count(text => text.Equals("Documentation", StringComparison.Ordinal)) == 1,
            $"{package.Document.DisplayName} does not expose its Documentation button.");
        var documentationPath = Path.Combine(Path.GetDirectoryName(package.Path)!,
            "Editor", "Doc", "index.html");
        Require(File.Exists(documentationPath),
            $"{package.Document.DisplayName} documentation is missing at {documentationPath}.");

        SetTab(selectedTab, view, "Dependencies");
        var dependencies = DetailText(Render(view));
        RequireTabs(dependencies, package.Document.DisplayName);
        var expectedDependencies = manager.GetDependencies(package.Document.Id);
        if (expectedDependencies.Count == 0)
            Require(dependencies.Contains("None", StringComparer.Ordinal),
                $"{package.Document.DisplayName} did not report its empty dependency set.");
        else
            foreach (var dependency in expectedDependencies)
                Require(dependencies.Contains(dependency.Document.DisplayName, StringComparer.Ordinal),
                    $"{package.Document.DisplayName} omitted dependency {dependency.Document.DisplayName}.");

        var examplesDirectory = Path.Combine(Path.GetDirectoryName(package.Path)!,
            "Editor", "Examples");
        var archives = FindExampleArchives(examplesDirectory);
        Require(archives.Length > 0, $"{package.Document.DisplayName} has no examples to present.");
        VerifyExampleView(view, windowType, selectedTab, package.Document.DisplayName, archives, 1,
            applicationType, application, workspace, attachHost, detachHost);
    }

    private static void VerifyExampleView(
        EditorWindow view,
        Type windowType,
        FieldInfo selectedTab,
        string packageName,
        IReadOnlyCollection<string> archives,
        int packageImportButtonCount,
        Type applicationType,
        object application,
        ProjectWorkspace workspace,
        MethodInfo attachHost,
        MethodInfo detachHost)
    {
        SetTab(selectedTab, view, "Examples");
        var examples = DetailText(Render(view));
        RequireTabs(examples, packageName);
        Require(examples.Count(text => text.Equals("Import", StringComparison.Ordinal)) ==
                archives.Count + packageImportButtonCount,
            $"{packageName} must show one Import button for every example.");

        var exampleModels = ((IEnumerable)(windowType.GetField("_examples", HiddenInstance)?.GetValue(view) ??
                                           throw new InvalidOperationException(
                                               $"{packageName} example models were not loaded.")))
            .Cast<object>().ToArray();
        Require(exampleModels.Length == archives.Count,
            $"{packageName} did not expose every example to Package Manager.");
        var displayNameProperty = exampleModels[0].GetType().GetProperty("DisplayName") ??
                                  throw new MissingMemberException(exampleModels[0].GetType().FullName,
                                      "DisplayName");
        VerifyExampleActionLayout(Render(view, NarrowPanelWidth, NarrowPanelHeight), exampleModels,
            displayNameProperty, "Import", packageName);
        var importExample = windowType.GetMethod("ImportExample", HiddenInstance) ??
                            throw new MissingMethodException(windowType.FullName, "ImportExample");
        var importPathProperty = exampleModels[0].GetType().GetProperty("ImportPath") ??
                                 throw new MissingMemberException(exampleModels[0].GetType().FullName, "ImportPath");
        attachHost.Invoke(null, [application]);
        SetField(applicationType, application, "_playing", true);
        try
        {
            foreach (var example in exampleModels)
            {
                var importPath = importPathProperty.GetValue(example) as string ?? string.Empty;
                var destination = Path.Combine(workspace.AssetsPath,
                    importPath.Replace('/', Path.DirectorySeparatorChar));
                Require(!Directory.Exists(destination),
                    $"{packageName} example destination unexpectedly existed before import.");
                importExample.Invoke(view, [example, false]);
                Require(!Directory.Exists(destination),
                    $"{packageName} imported an example while Play Mode was active.");
            }
        }
        finally
        {
            SetField(applicationType, application, "_playing", false);
            detachHost.Invoke(null, [application]);
        }
        var importLogs = new List<LogEntry>();
        void CaptureImportLog(LogEntry entry) => importLogs.Add(entry);
        BEngine.Debug.MessageLogged += CaptureImportLog;
        try
        {
            foreach (var example in exampleModels) importExample.Invoke(view, [example, false]);
        }
        finally
        {
            BEngine.Debug.MessageLogged -= CaptureImportLog;
        }

        examples = DetailText(Render(view));
        Require(examples.Count(text => text.Equals("Reimport", StringComparison.Ordinal)) == archives.Count,
            $"{packageName} must change every imported example action to Reimport. " +
            $"Import logs: {string.Join(" | ", importLogs.Select(entry => entry.Message))}");
        Require(examples.Count(text => text.Equals("Import", StringComparison.Ordinal)) == packageImportButtonCount,
            $"{packageName} lost or duplicated its package Import button after example import.");
        VerifyExampleActionLayout(Render(view, NarrowPanelWidth, NarrowPanelHeight), exampleModels,
            displayNameProperty, "Reimport", packageName);
    }

    private static void VerifyExampleActionLayout(
        IReadOnlyList<GpuCanvasCommand> commands,
        IReadOnlyCollection<object> examples,
        PropertyInfo displayNameProperty,
        string actionLabel,
        string packageName)
    {
        foreach (var example in examples)
        {
            var displayName = displayNameProperty.GetValue(example) as string ??
                              throw new InvalidOperationException($"{packageName} has an unnamed example.");
            var nameCommands = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                          command.Content.Equals(displayName,
                                                              StringComparison.Ordinal)).ToArray();
            Require(nameCommands.Length == 1,
                $"{packageName}/{displayName} must have exactly one visible name in a narrow detail panel.");
            var name = nameCommands[0];
            var actions = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Content.Equals(actionLabel,
                                                         StringComparison.Ordinal) &&
                                                     SharesRow(command.Rect, name.Rect)).ToArray();
            Require(actions.Length == 1,
                $"{packageName}/{displayName} must have one {actionLabel} button on its example row.");
            var action = actions[0];
            var buttonSurfaces = commands.Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                            SameRect(command.ClipRect, action.ClipRect) &&
                                                            IsInside(action.Rect, command.Rect) &&
                                                            SharesRow(command.Rect, action.Rect))
                .OrderBy(command => command.Rect.Width).ToArray();
            Require(buttonSurfaces.Length > 0,
                $"{packageName}/{displayName} {actionLabel} action does not have a visible button surface.");
            var button = buttonSurfaces[0].Rect;
            Require(button.X >= name.Rect.Right - GeometryTolerance,
                $"{packageName}/{displayName} {actionLabel} button must be to the right of its name: " +
                $"name={name.Rect}, button={button}.");
            Require(IsInside(button, action.ClipRect),
                $"{packageName}/{displayName} {actionLabel} button is clipped outside the narrow details panel: " +
                $"button={button}, clip={action.ClipRect}.");
            Require(SameRect(name.ClipRect, action.ClipRect),
                $"{packageName}/{displayName} name and {actionLabel} button are not in the same details clip.");
        }
    }

    private static bool SharesRow(GpuCanvasRect left, GpuCanvasRect right) =>
        Math.Abs((left.Y + left.Height / 2) - (right.Y + right.Height / 2)) <= GeometryTolerance;

    private static bool IsInside(GpuCanvasRect inner, GpuCanvasRect outer) =>
        inner.X >= outer.X - GeometryTolerance && inner.Y >= outer.Y - GeometryTolerance &&
        inner.Right <= outer.Right + GeometryTolerance && inner.Bottom <= outer.Bottom + GeometryTolerance;

    private static bool SameRect(GpuCanvasRect left, GpuCanvasRect right) =>
        Math.Abs(left.X - right.X) <= GeometryTolerance && Math.Abs(left.Y - right.Y) <= GeometryTolerance &&
        Math.Abs(left.Width - right.Width) <= GeometryTolerance &&
        Math.Abs(left.Height - right.Height) <= GeometryTolerance;

    private static string[] FindExampleArchives(string? directory) =>
        string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)
            ? []
            : Directory.EnumerateFiles(directory, "*.bpackage", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

    private static void SetTab(FieldInfo selectedTab, object view, string name) =>
        selectedTab.SetValue(view, Enum.Parse(selectedTab.FieldType, name));

    private static IReadOnlyList<string> DetailText(IEnumerable<GpuCanvasCommand> commands) => commands
        .Where(command => command.Type == GpuCanvasCommandType.Text && command.Rect.X > 360)
        .Select(command => command.Content).ToArray();

    private static IReadOnlyList<GpuCanvasCommand> Render(EditorWindow view, int width = 1_200, int height = 2_000)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [new Event(EventType.Repaint), width, height, commands]);
        try { DrawWindow.Invoke(view, null); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private static void RequireTabs(IReadOnlyList<string> text, string packageName)
    {
        foreach (var tab in new[] { "Description", "Dependencies", "Examples" })
            Require(text.Contains(tab, StringComparer.Ordinal),
                $"{packageName} detail view is missing the {tab} tab.");
    }

    private static void SetField(Type type, object target, string name, object? value) =>
        (type.GetField(name, HiddenInstance) ?? throw new MissingFieldException(type.FullName, name))
        .SetValue(target, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
