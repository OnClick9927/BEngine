using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using InspectorEditor = BEngine.Editor.Editor;

namespace BEngine.ExampleTests.ProjectAssetWorkflow;

internal static class Program
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                         BindingFlags.NonPublic;
    private static IReadOnlyList<string> _capturedMenuPaths = [];

    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--unified-asset-only", StringComparer.Ordinal))
            {
                UnifiedBAssetArchitectureTests.Run();
                return 0;
            }
            if (args.Contains("--basset-only", StringComparer.Ordinal))
            {
                UnifiedBAssetArchitectureTests.Run();
                BAssetTypeSystemTests.Run();
                return 0;
            }
            var editorAssembly = typeof(EditorWindow).Assembly;
            var (applicationType, projectWindowType, itemType) = DiscoverProjectTypes(editorAssembly);

            VerifyProjectTypeSearch(projectWindowType, itemType);
            VerifyProjectObjectDragSource(projectWindowType, itemType);
            VerifyCreateMenuUsesAssetMetadata(editorAssembly, projectWindowType);
            AssetMenuIntegrationTests.Run(editorAssembly, applicationType, projectWindowType, itemType);
            VerifyProjectSelectionsFeedInspector(editorAssembly, itemType);
            VerifyItemContextMenuHasVisibleText(editorAssembly, applicationType, projectWindowType, itemType);
            UnifiedBAssetArchitectureTests.Run();
            BAssetTypeSystemTests.Run();

            Console.WriteLine(
                "PROJECT_ASSET_WORKFLOW_OK|assets-folder-inspector,assets-file-inspector,package-folder-inspector," +
                "package-file-inspector,full-metadata,context-menu-text,type-search,unified-assets-create," +
                "extended-create-types,assets-utilities,scene,script,typed-load,importer-meta-roundtrip," +
                "dynamic-icon,basset-reference-roundtrip,project-object-drag");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PROJECT_ASSET_WORKFLOW_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyProjectTypeSearch(Type projectWindowType, Type itemType)
    {
        var projectWindow = Activator.CreateInstance(projectWindowType, nonPublic: true) ??
                            throw new InvalidOperationException("Project window could not be created.");
        var matches = projectWindowType.GetMethods(InstanceMembers).Single(method =>
            method.Name.Equals("Matches", StringComparison.Ordinal) &&
            method.ReturnType == typeof(bool) &&
            method.GetParameters() is [{ ParameterType: var parameterType }] &&
            parameterType == itemType);

        var script = CreateItem(itemType, "Assets/Scripts/Player.cs", "Player.cs", "Script", false, false);
        var prefab = CreateItem(itemType, "Assets/Prefabs/Player.prefab.yaml", "Player.prefab.yaml", "Prefab",
            false, false);
        var scene = CreateItem(itemType, "Assets/Scenes/Player.scene.yaml", "Player.scene.yaml", "Scene",
            false, false);
        var folder = CreateItem(itemType, "Assets/Scripts", "Scripts", "Folder", true, false);
        var packageScript = CreateItem(itemType, "Packages/com.test/Runtime/Enemy.cs", "Enemy.cs", "Script",
            false, true);
        var searchField = FindSearchField(projectWindowType, projectWindow, matches, script, prefab);

        Require(Matches("t:Script", script) && Matches("t:Script", packageScript) && !Matches("t:Script", prefab),
            "Project t:Script did not filter Assets and Packages by resource type.");
        Require(Matches("Player t:Script", script) && !Matches("Enemy t:Script", script),
            "Project search did not combine a name term with t:Type.");
        Require(Matches("t:GameObject", prefab) && !Matches("t:GameObject", scene),
            "Project t:GameObject did not resolve the prefab main-object alias.");
        Require(Matches("t:Folder", folder) && !Matches("t:Folder", script),
            "Project t:Folder did not isolate directory resources.");

        return;

        bool Matches(string filter, object item)
        {
            searchField.SetValue(projectWindow, filter);
            return matches.Invoke(projectWindow, [item]) is true;
        }
    }

    private static void VerifyProjectObjectDragSource(Type projectWindowType, Type itemType)
    {
        var canStart = projectWindowType.GetMethod("CanStartObjectDrag",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(projectWindowType.FullName, "CanStartObjectDrag");
        var dragObject = projectWindowType.GetMethod("DragObject",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(projectWindowType.FullName, "DragObject");
        var source = Path.Combine(Path.GetTempPath(), $"BEnginePackageDrag-{Guid.NewGuid():N}.asset.yaml");
        File.WriteAllText(source, "name: Package Drag Fixture");
        try
        {
            var packageItem = CreateItem(itemType, "Packages/com.test/Package.asset.yaml",
                "Package.asset.yaml", "YamlAsset", false, true, source);
            Require(canStart.Invoke(null, [packageItem]) is true,
                "A readable package asset could not start an ObjectField drag from Project.");
            Require(dragObject.Invoke(null, [packageItem]) is DefaultAsset
                    { assetPath: "Packages/com.test/Package.asset.yaml" },
                "Project did not expose a stable read-only BAsset payload for a package drag.");

            var packagesRoot = CreateItem(itemType, "Packages", "Packages", "Folder", true, true,
                Path.GetDirectoryName(source));
            Require(canStart.Invoke(null, [packagesRoot]) is false,
                "The synthetic Packages root incorrectly started an ObjectField drag.");
        }
        finally
        {
            File.Delete(source);
        }
    }

    private static void VerifyCreateMenuUsesAssetMetadata(Assembly editorAssembly, Type projectWindowType)
    {
        var projectWindow = Activator.CreateInstance(projectWindowType, nonPublic: true) ??
                            throw new InvalidOperationException("Project window could not be created.");
        var dispatcherType = editorAssembly.GetType(
            "BEngine.Editor.GenericMenuDispatcher", throwOnError: true)!;
        var handler = dispatcherType.GetProperty(
            "Handler", BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMemberException(dispatcherType.FullName, "Handler");
        var previous = handler.GetValue(null);
        try
        {
            _capturedMenuPaths = [];
            handler.SetValue(null, BuildMenuCaptureDelegate(handler.PropertyType));
            var candidates = projectWindowType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(method => method.ReturnType == typeof(void) &&
                                 method.GetParameters() is [{ ParameterType: var parameterType }] &&
                                 parameterType == typeof(Rect));
            foreach (var candidate in candidates)
            {
                _capturedMenuPaths = [];
                try { candidate.Invoke(projectWindow, [new Rect(0, 0, 24, 20)]); }
                catch (TargetInvocationException) { continue; }
                if (_capturedMenuPaths.Count > 0) break;
            }
        }
        finally
        {
            handler.SetValue(null, previous);
        }

        Require(_capturedMenuPaths.Contains("Folder", StringComparer.Ordinal),
            "Project Create menu lost Folder.");
        Require(_capturedMenuPaths.Contains("Assembly Definition", StringComparer.Ordinal),
            "Project Create menu lost Assembly Definition.");
        Require(_capturedMenuPaths.Contains("C# Script", StringComparer.Ordinal),
            "Project Create menu does not provide C# Script.");
        Require(_capturedMenuPaths.Contains("Scene", StringComparer.Ordinal),
            "Project Create menu does not provide Scene.");
        Require(_capturedMenuPaths.Contains("Testing/Workflow Asset", StringComparer.Ordinal),
            "Project Create menu did not consume CreateAssetMenuAttribute metadata.");
    }

    private static (Type ApplicationType, Type ProjectWindowType, Type ItemType) DiscoverProjectTypes(
        Assembly editorAssembly)
    {
        var applicationType = editorAssembly.GetTypes().FirstOrDefault(type =>
            type.GetNestedTypes(BindingFlags.NonPublic).Any(nested =>
                typeof(EditorWindow).IsAssignableFrom(nested) &&
                nested.GetMethod("CaptureLayout", InstanceMembers) is not null)) ??
            throw new TypeLoadException("The GPU editor application type was not found.");
        var projectWindowType = applicationType.GetNestedTypes(BindingFlags.NonPublic).Single(type =>
            typeof(EditorWindow).IsAssignableFrom(type) &&
            type.GetMethod("CaptureLayout", InstanceMembers) is not null &&
            type.GetMethod("ApplyLayout", InstanceMembers) is not null);
        var itemType = projectWindowType.GetMethods(InstanceMembers)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .First(type => type.GetProperty("VirtualPath", InstanceMembers) is not null &&
                           type.GetProperty("DisplayName", InstanceMembers) is not null);
        return (applicationType, projectWindowType, itemType);
    }

    private static FieldInfo FindSearchField(
        Type projectWindowType,
        object projectWindow,
        MethodInfo matches,
        object matchingItem,
        object nonMatchingItem)
    {
        foreach (var field in projectWindowType.GetFields(InstanceMembers).Where(field =>
                     field.FieldType == typeof(string)))
        {
            var previous = field.GetValue(projectWindow);
            try
            {
                field.SetValue(projectWindow, "t:Script");
                if (matches.Invoke(projectWindow, [matchingItem]) is true &&
                    matches.Invoke(projectWindow, [nonMatchingItem]) is false)
                    return field;
            }
            finally { field.SetValue(projectWindow, previous); }
        }
        throw new MissingMemberException(projectWindowType.FullName, "semantic Project search field");
    }

    private static void VerifyProjectSelectionsFeedInspector(Assembly editorAssembly, Type itemType)
    {
        var selectionFactory = editorAssembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .SingleOrDefault(method => method.ReturnType == typeof(DefaultAsset) &&
                                       method.GetParameters() is [{ ParameterType: var parameterType }] &&
                                       parameterType == itemType) ??
            throw new MissingMethodException("No ProjectBrowserItem-to-DefaultAsset selection factory was found.");
        var fixtures = new[]
        {
            new { Path = "Assets/Scenes", Name = "Scenes", Type = "Folder", Directory = true, Package = false },
            new { Path = "Assets/Scenes/Main.scene.yaml", Name = "Main.scene.yaml", Type = "Scene", Directory = false, Package = false },
            new { Path = "Packages/com.test/Runtime", Name = "Runtime", Type = "Folder", Directory = true, Package = true },
            new { Path = "Packages/com.test/Runtime/Probe.cs", Name = "Probe.cs", Type = "Script", Directory = false, Package = true }
        };

        foreach (var fixture in fixtures)
        {
            var source = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BEngineProjectSelection",
                fixture.Path.Replace('/', Path.DirectorySeparatorChar)));
            var item = CreateItem(itemType, fixture.Path, fixture.Name, fixture.Type,
                fixture.Directory, fixture.Package, source);
            var asset = selectionFactory.Invoke(null, [item]) as DefaultAsset ??
                        throw new InvalidOperationException($"Selection factory returned no asset for {fixture.Path}.");
            Require(asset.name == fixture.Name && asset.assetPath == fixture.Path &&
                    asset.sourcePath == source && asset.assetType == fixture.Type,
                $"Inspector selection metadata is incomplete for '{fixture.Path}'.");
            Require(!string.IsNullOrWhiteSpace(asset.name) && !string.IsNullOrWhiteSpace(asset.assetPath) &&
                    !string.IsNullOrWhiteSpace(asset.sourcePath) && !string.IsNullOrWhiteSpace(asset.assetType),
                $"Inspector selection contains an empty name/path/type field for '{fixture.Path}'.");
            if (fixture.Package)
            {
                Require(asset.packageId == "com.test" && asset.packageVersion == "1.0.0",
                    $"Package Inspector selection lost package metadata for '{fixture.Path}'.");
                Require((asset.hideFlags & HideFlags.NotEditable) != 0,
                    $"Managed package content is editable in Inspector: '{fixture.Path}'.");
            }

            Selection.activeObject = asset;
            Require(ReferenceEquals(Selection.activeObject, asset),
                $"Selection did not retain the Inspector object for '{fixture.Path}'.");
            using var inspector = InspectorEditor.CreateEditor(asset);
            Require(inspector is DefaultAssetEditor,
                $"No DefaultAsset Inspector was created for '{fixture.Path}'.");
        }
    }

    private static void VerifyItemContextMenuHasVisibleText(
        Assembly editorAssembly,
        Type applicationType,
        Type projectWindowType,
        Type itemType)
    {
        var application = RuntimeHelpers.GetUninitializedObject(applicationType);
        var projectWindow = Activator.CreateInstance(
            projectWindowType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [application],
            culture: null) ?? throw new InvalidOperationException("Project window could not be created.");
        var item = CreateItem(itemType, "Packages/com.test/Runtime", "Runtime", "Folder", true, true);
        var dispatcherType = editorAssembly.GetType(
            "BEngine.Editor.GenericMenuDispatcher", throwOnError: true)!;
        var handler = dispatcherType.GetProperty(
            "Handler", BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMemberException(dispatcherType.FullName, "Handler");
        var previous = handler.GetValue(null);
        try
        {
            handler.SetValue(null, BuildMenuCaptureDelegate(handler.PropertyType));
            var candidates = projectWindowType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(method => method.ReturnType == typeof(void) &&
                                 method.GetParameters() is [{ ParameterType: var parameterType }] &&
                                 parameterType == itemType)
                .ToArray();
            foreach (var candidate in candidates)
            {
                _capturedMenuPaths = [];
                try { candidate.Invoke(projectWindow, [item]); }
                catch (TargetInvocationException) { continue; }
                if (_capturedMenuPaths.Count > 0) break;
            }
        }
        finally
        {
            handler.SetValue(null, previous);
        }

        Require(_capturedMenuPaths.Count > 0,
            "Right-clicking a Project item produced no GenericMenu entries.");
        Require(_capturedMenuPaths.All(path => !string.IsNullOrWhiteSpace(path)),
            "A Project item context menu contains an icon-only or empty-text command.");
        Require(_capturedMenuPaths.Any(path => path.Contains("Open", StringComparison.OrdinalIgnoreCase)) &&
                _capturedMenuPaths.Any(path => path.Contains("Copy", StringComparison.OrdinalIgnoreCase)),
            "Project item context menu lost its visible Open or Copy actions.");
    }

    private static object CreateItem(
        Type itemType,
        string virtualPath,
        string displayName,
        string assetType,
        bool isDirectory,
        bool isPackage,
        string? sourcePath = null) =>
        Activator.CreateInstance(
            itemType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                virtualPath,
                displayName,
                sourcePath ?? Path.GetFullPath(virtualPath.Replace('/', Path.DirectorySeparatorChar)),
                assetType,
                isDirectory,
                isPackage,
                null,
                isPackage ? "com.test" : null,
                isPackage ? "1.0.0" : null
            ],
            culture: null) ?? throw new InvalidOperationException($"Could not create {itemType.FullName}.");

    private static Delegate BuildMenuCaptureDelegate(Type delegateType)
    {
        var parameterType = delegateType.GetMethod("Invoke")!.GetParameters().Single().ParameterType;
        var parameter = Expression.Parameter(parameterType, "items");
        var capture = typeof(Program).GetMethod(nameof(CaptureMenu),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return Expression.Lambda(delegateType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile();
    }

    private static void CaptureMenu(object items)
    {
        var paths = new List<string>();
        foreach (var item in (IEnumerable)items)
        {
            var path = item.GetType().GetProperty("Path")?.GetValue(item) as string;
            var separator = item.GetType().GetProperty("Separator")?.GetValue(item) is true;
            if (!separator) paths.Add(path ?? string.Empty);
        }
        _capturedMenuPaths = paths;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
