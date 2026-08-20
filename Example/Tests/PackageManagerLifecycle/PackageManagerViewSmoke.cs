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
            foreach (var package in manager.definitions)
                VerifyPackageView(view, window, selectedTab, package, manager);
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
        BPackageManager manager)
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
            "EditorResources", "Doc", "index.html");
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
            "EditorResources", "Examples");
        var archives = Directory.Exists(examplesDirectory)
            ? Directory.EnumerateFiles(examplesDirectory, "*.bpackage", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        Require(archives.Length > 0, $"{package.Document.DisplayName} has no examples to present.");

        SetTab(selectedTab, view, "Examples");
        var examples = DetailText(Render(view));
        RequireTabs(examples, package.Document.DisplayName);
        Require(examples.Count(text => text.Equals("Import", StringComparison.Ordinal)) == archives.Length + 1,
            $"{package.Document.DisplayName} must show one Import button for the package and each example.");

        var exampleModels = ((IEnumerable)(windowType.GetField("_examples", HiddenInstance)?.GetValue(view) ??
                                           throw new InvalidOperationException(
                                               $"{package.Document.DisplayName} example models were not loaded.")))
            .Cast<object>().ToArray();
        Require(exampleModels.Length == archives.Length,
            $"{package.Document.DisplayName} did not expose every example to Package Manager.");
        var importExample = windowType.GetMethod("ImportExample", HiddenInstance) ??
                            throw new MissingMethodException(windowType.FullName, "ImportExample");
        foreach (var example in exampleModels) importExample.Invoke(view, [example, false]);

        examples = DetailText(Render(view));
        Require(examples.Count(text => text.Equals("Reimport", StringComparison.Ordinal)) == archives.Length,
            $"{package.Document.DisplayName} must change every imported example action to Reimport.");
        Require(examples.Count(text => text.Equals("Import", StringComparison.Ordinal)) == 1,
            $"{package.Document.DisplayName} lost or duplicated its package Import button after example import.");
    }

    private static void SetTab(FieldInfo selectedTab, object view, string name) =>
        selectedTab.SetValue(view, Enum.Parse(selectedTab.FieldType, name));

    private static IReadOnlyList<string> DetailText(IEnumerable<GpuCanvasCommand> commands) => commands
        .Where(command => command.Type == GpuCanvasCommandType.Text && command.Rect.X > 360)
        .Select(command => command.Content).ToArray();

    private static IReadOnlyList<GpuCanvasCommand> Render(EditorWindow view)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [new Event(EventType.Repaint), 1_200, 2_000, commands]);
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
