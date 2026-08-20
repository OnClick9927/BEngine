using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.ProjectAssetWorkflow;

internal static class AssetMenuIntegrationTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                         BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public |
                                                       BindingFlags.NonPublic;

    private static IReadOnlyList<MenuSnapshot> _capturedMenu = [];

    internal static void Run(
        Assembly editorAssembly,
        Type applicationType,
        Type projectWindowType,
        Type itemType)
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineAssetMenu_{Guid.NewGuid():N}");
        object? application = null;
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(root, "Asset Menu Integration");
            var assets = new ProjectAssetDatabase(workspace);
            assets.Refresh();
            application = CreateApplication(editorAssembly, applicationType, projectWindowType, workspace, assets);
            AttachEditorHost(editorAssembly, application);
            var folderContext = CaptureFolderContextMenu(editorAssembly, application, projectWindowType, itemType,
                workspace, out var contextWindow, out var folderPath, out var sourcePath);
            var toolbarCreate = CaptureProjectCreateMenu(editorAssembly, projectWindowType);
            var assetsMenu = CaptureAssetsMenu(applicationType, application);

            VerifyCoreMenuOwnership(editorAssembly);
            VerifyUnifiedCreateMenus(toolbarCreate, folderContext, assetsMenu);
            VerifyUsefulAssetCommands(folderContext, assetsMenu, contextWindow, folderPath, sourcePath);
            VerifyCreationTemplates(editorAssembly, workspace, folderPath);
        }
        finally
        {
            if (application is not null) DetachEditorHost(editorAssembly, application);
            TryDelete(root);
        }
    }

    private static void VerifyCoreMenuOwnership(Assembly editorAssembly)
    {
        var optionalPackageMarkers = new[]
        {
            "Animation", "Navigation", "Physics", "Property Attributes", "UI Toolkit", "UIElements"
        };
        var violations = editorAssembly.GetTypes()
            .SelectMany(type => type.GetMethods(StaticMembers))
            .SelectMany(method => method.GetCustomAttributes<MenuItemAttribute>())
            .Select(attribute => attribute.itemName.Replace('\\', '/'))
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) ||
                           path.StartsWith("GameObject/", StringComparison.Ordinal) ||
                           path.StartsWith("Component/", StringComparison.Ordinal))
            .Where(path => optionalPackageMarkers.Any(marker =>
                path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Require(violations.Length == 0,
            $"Core owns optional-package menu commands: {string.Join(", ", violations)}");
    }

    private static object CreateApplication(
        Assembly editorAssembly,
        Type applicationType,
        Type projectWindowType,
        ProjectWorkspace workspace,
        ProjectAssetDatabase assets)
    {
        var application = RuntimeHelpers.GetUninitializedObject(applicationType);
        var registryType = editorAssembly.GetType("BEngine.Editor.MenuItemRegistry", throwOnError: true)!;
        var registry = registryType.GetMethod("Discover", StaticMembers)?.Invoke(null, null) ??
                       throw new MissingMethodException(registryType.FullName, "Discover");
        SetField(application, "_menuItems", registry);
        SetField(application, "_workspace", workspace);
        SetField(application, "_assets", assets);

        var projectWindow = Activator.CreateInstance(
            projectWindowType,
            InstanceMembers,
            binder: null,
            args: [application],
            culture: null) ?? throw new InvalidOperationException("Project window could not be created.");
        SetField(application, "_project", projectWindow);
        return application;
    }

    private static IReadOnlyList<MenuSnapshot> CaptureProjectCreateMenu(
        Assembly editorAssembly,
        Type projectWindowType)
    {
        var projectWindow = Activator.CreateInstance(projectWindowType, nonPublic: true) ??
                            throw new InvalidOperationException("Project window could not be created.");
        var showCreateMenu = projectWindowType.GetMethod("ShowCreateMenu", InstanceMembers,
                                 binder: null, types: [typeof(Rect)], modifiers: null) ??
                             throw new MissingMethodException(projectWindowType.FullName, "ShowCreateMenu");
        return CaptureGenericMenu(editorAssembly,
            () => showCreateMenu.Invoke(projectWindow, [new Rect(0, 0, 24, 20)]));
    }

    private static IReadOnlyList<MenuSnapshot> CaptureFolderContextMenu(
        Assembly editorAssembly,
        object application,
        Type projectWindowType,
        Type itemType,
        ProjectWorkspace workspace,
        out object projectWindow,
        out string folderPath,
        out string sourcePath)
    {
        folderPath = "Assets/Menu Integration";
        sourcePath = Path.Combine(workspace.AssetsPath, "Menu Integration");
        Directory.CreateDirectory(sourcePath);
        var record = new AssetRecord(Guid.NewGuid(), folderPath, sourcePath, sourcePath + ".meta",
            string.Empty, "Folder", string.Empty, true);
        var item = Activator.CreateInstance(
            itemType,
            InstanceMembers,
            binder: null,
            args: [folderPath, "Menu Integration", sourcePath, "Folder", true, false, record, null, null],
            culture: null) ?? throw new InvalidOperationException($"Could not create {itemType.FullName}.");
        projectWindow = Activator.CreateInstance(
            projectWindowType,
            InstanceMembers,
            binder: null,
            args: [application],
            culture: null) ?? throw new InvalidOperationException("Project window could not be created.");
        var cache = Array.CreateInstance(itemType, 1);
        cache.SetValue(item, 0);
        SetField(projectWindow, "_cache", cache);
        SetField(projectWindow, "_selectedPath", folderPath);
        SetField(application, "_project", projectWindow);
        var showContextMenu = projectWindowType.GetMethod("ShowItemContextMenu", InstanceMembers,
                                  binder: null, types: [itemType], modifiers: null) ??
                              throw new MissingMethodException(projectWindowType.FullName, "ShowItemContextMenu");
        var capturedWindow = projectWindow;
        return CaptureGenericMenu(editorAssembly, () => showContextMenu.Invoke(capturedWindow, [item]));
    }

    private static void VerifyCreationTemplates(
        Assembly editorAssembly,
        ProjectWorkspace workspace,
        string folderPath)
    {
        var creationType = editorAssembly.GetType("BEngine.Editor.ProjectAssetCreation", throwOnError: true)!;
        var expected = new[]
        {
            new CreationExpectation("CreateScriptableObjectScript", "NewScriptableObject.cs", ": ScriptableObject"),
            new CreationExpectation("CreateText", "New Text File.txt", string.Empty),
            new CreationExpectation("CreateMarkdown", "New Markdown.md", "# New Markdown"),
            new CreationExpectation("CreateJson", "New Data.json", "{}"),
            new CreationExpectation("CreateYaml", "New Data.yaml", "---")
        };

        foreach (var item in expected)
        {
            var path = InvokeCreator(creationType, item.Method, folderPath);
            Require(path == $"{folderPath}/{item.FileName}",
                $"{item.Method} created an unexpected asset path: {path}");
            var fullPath = Path.Combine(workspace.RootPath, path.Replace('/', Path.DirectorySeparatorChar));
            Require(File.Exists(fullPath), $"{item.Method} did not create '{path}'.");
            var content = File.ReadAllText(fullPath);
            Require(item.ContentFragment.Length == 0 || content.Contains(item.ContentFragment, StringComparison.Ordinal),
                $"{item.Method} did not write a usable {item.FileName} template.");
        }

        var uniqueJson = InvokeCreator(creationType, "CreateJson", folderPath);
        Require(uniqueJson == $"{folderPath}/New Data 1.json" &&
                File.Exists(Path.Combine(workspace.RootPath,
                    uniqueJson.Replace('/', Path.DirectorySeparatorChar))),
            "Creating the same resource type twice did not generate a unique asset path.");
    }

    private static string InvokeCreator(Type creationType, string methodName, string folderPath)
    {
        var method = creationType.GetMethod(methodName, StaticMembers,
                         binder: null, types: [typeof(string)], modifiers: null) ??
                     throw new MissingMethodException(creationType.FullName, methodName);
        try
        {
            return method.Invoke(null, [folderPath]) as string ??
                   throw new InvalidOperationException($"{methodName} returned no asset path.");
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static void AttachEditorHost(Assembly editorAssembly, object application)
    {
        var bridge = editorAssembly.GetType("BEngine.Editor.EditorBridge", throwOnError: true)!;
        bridge.GetMethod("Attach", StaticMembers)?.Invoke(null, [application]);
    }

    private static void DetachEditorHost(Assembly editorAssembly, object application)
    {
        var bridge = editorAssembly.GetType("BEngine.Editor.EditorBridge", throwOnError: true)!;
        bridge.GetMethod("Detach", StaticMembers)?.Invoke(null, [application]);
    }

    private static IReadOnlyList<MenuSnapshot> CaptureAssetsMenu(Type applicationType, object application)
    {
        var menuItems = applicationType.GetMethod("MenuItems", InstanceMembers,
                            binder: null, types: [typeof(string)], modifiers: null) ??
                        throw new MissingMethodException(applicationType.FullName, "MenuItems");
        var result = menuItems.Invoke(application, ["Assets"]) as IEnumerable ??
                     throw new InvalidOperationException("Assets main menu returned no entries.");
        return result.Cast<object>().Select(item => new MenuSnapshot(
            Read<string>(item, "Label"),
            Read<bool>(item, "Enabled"),
            Read<Delegate?>(item, "Action"))).ToArray();
    }

    private static void VerifyUnifiedCreateMenus(
        IReadOnlyList<MenuSnapshot> toolbarCreate,
        IReadOnlyList<MenuSnapshot> folderContext,
        IReadOnlyList<MenuSnapshot> assetsMenu)
    {
        var toolbarPaths = toolbarCreate.Select(item => NormalizePath(item.Path)).ToHashSet(StringComparer.Ordinal);
        var contextPaths = folderContext
            .Where(item => item.Path.StartsWith("Create/", StringComparison.Ordinal))
            .Select(item => NormalizePath(item.Path["Create/".Length..]))
            .ToHashSet(StringComparer.Ordinal);
        var assetsPaths = assetsMenu
            .Where(item => item.Path.StartsWith("Create/", StringComparison.Ordinal))
            .Select(item => NormalizePath(item.Path["Create/".Length..]))
            .ToHashSet(StringComparer.Ordinal);

        Require(toolbarPaths.Count >= 12,
            $"Project Create still exposes only {toolbarPaths.Count} resource types; expected at least 12.");
        Require(toolbarPaths.SetEquals(contextPaths),
            "Project toolbar Create and folder right-click Create do not use the same command set. " +
            DescribeDifference(toolbarPaths, contextPaths));
        Require(toolbarPaths.SetEquals(assetsPaths),
            "Assets/Create and Project Create do not use the same command set. " +
            DescribeDifference(toolbarPaths, assetsPaths));

        foreach (var requiredPath in new[]
                 {
                     "Folder",
                     "C# Script",
                     "Scripting/ScriptableObject Script",
                     "Scene",
                     "Prefab",
                     "Assembly Definition",
                     "Shader",
                     "Text/Text File",
                     "Text/Markdown File",
                     "Data/JSON File",
                     "Data/YAML File",
                     "Testing/Workflow Asset"
                 })
            Require(toolbarPaths.Contains(requiredPath),
                $"Unified Assets/Create menu does not provide '{requiredPath}'.");
        Require(!toolbarPaths.Contains("Scripting/Editor Window Script"),
            "Assets/Create exposes an Editor Window script where the assembly boundary is not guaranteed.");
        Require(!toolbarPaths.Any(path => path.StartsWith("UI Toolkit/", StringComparison.Ordinal)),
            "Core Assets/Create exposes UIElements commands; optional packages must register their own menus.");

        Require(toolbarCreate.Where(item => !string.IsNullOrWhiteSpace(item.Path)).All(item =>
                item.Enabled && item.Action is not null),
            "An Assets-folder Create command is visible but cannot be executed.");
    }

    private static void VerifyUsefulAssetCommands(
        IReadOnlyList<MenuSnapshot> folderContext,
        IReadOnlyList<MenuSnapshot> assetsMenu,
        object contextWindow,
        string folderPath,
        string sourcePath)
    {
        foreach (var command in new[]
                 {
                     AssetCommand.Open,
                     AssetCommand.ShowInExplorer,
                     AssetCommand.CopyPath,
                     AssetCommand.CopyFullPath,
                     AssetCommand.Rename,
                     AssetCommand.Duplicate,
                     AssetCommand.Delete,
                     AssetCommand.Reimport,
                     AssetCommand.Refresh
                 })
        {
            Require(FindCommand(folderContext, command) is not null,
                $"Project asset context menu does not provide {Describe(command)}.");
            Require(FindCommand(assetsMenu, command) is not null,
                $"Assets menu does not provide {Describe(command)}.");
        }

        Require(assetsMenu.Any(item => IsImportPackage(item.Path) && item.Action is not null),
            "Assets menu does not provide Import Package.");
        Require(assetsMenu.Any(item => IsExportPackage(item.Path) && item.Action is not null),
            "Assets menu does not provide Export Package.");
        Require(assetsMenu.Any(item => IsImportNewAsset(item.Path) && item.Action is not null),
            "Assets menu does not provide Import New Asset.");

        var copyPath = FindCommand(folderContext, AssetCommand.CopyPath)!;
        var copyPathAction = copyPath.Action ??
                             throw new InvalidOperationException("Project Copy Path has no action.");
        Require(copyPath.Enabled,
            "Project Copy Path is disabled for an editable Assets folder.");
        GUIUtility.systemCopyBuffer = string.Empty;
        copyPathAction.DynamicInvoke();
        Require(GUIUtility.systemCopyBuffer == folderPath,
            "Project Copy Path did not copy the selected asset's project-relative path.");

        var copyFullPath = FindCommand(folderContext, AssetCommand.CopyFullPath)!;
        var copyFullPathAction = copyFullPath.Action ??
                                 throw new InvalidOperationException("Project Copy Full Path has no action.");
        Require(copyFullPath.Enabled,
            "Project Copy Full Path is disabled for an editable Assets folder.");
        GUIUtility.systemCopyBuffer = string.Empty;
        copyFullPathAction.DynamicInvoke();
        Require(GUIUtility.systemCopyBuffer == sourcePath,
            "Project Copy Full Path did not copy the selected asset's source path.");

        var rename = FindCommand(folderContext, AssetCommand.Rename)!;
        var renameAction = rename.Action ??
                           throw new InvalidOperationException("Project Rename has no action.");
        Require(rename.Enabled,
            "Project Rename is disabled for an editable Assets folder.");
        renameAction.DynamicInvoke();
        var renamingPath = contextWindow.GetType().GetField("_renamingPath", InstanceMembers)?.GetValue(contextWindow);
        Require(renamingPath as string == folderPath,
            "Project Rename did not enter inline rename mode for the context-clicked asset.");
    }

    private static MenuSnapshot? FindCommand(IEnumerable<MenuSnapshot> items, AssetCommand command) =>
        items.FirstOrDefault(item => MatchesCommand(item.Path, command));

    private static bool MatchesCommand(string path, AssetCommand command)
    {
        var leaf = LeafName(path).TrimEnd('.');
        return command switch
        {
            AssetCommand.Open => leaf.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                                 leaf.Equals("Open Folder", StringComparison.OrdinalIgnoreCase),
            AssetCommand.ShowInExplorer => leaf.Contains("Explorer", StringComparison.OrdinalIgnoreCase) &&
                                           (leaf.Contains("Show", StringComparison.OrdinalIgnoreCase) ||
                                            leaf.Contains("Reveal", StringComparison.OrdinalIgnoreCase)),
            AssetCommand.CopyPath => leaf.Contains("Copy", StringComparison.OrdinalIgnoreCase) &&
                                     leaf.Contains("Path", StringComparison.OrdinalIgnoreCase) &&
                                     !leaf.Contains("Full", StringComparison.OrdinalIgnoreCase),
            AssetCommand.CopyFullPath => leaf.Contains("Copy", StringComparison.OrdinalIgnoreCase) &&
                                         leaf.Contains("Full", StringComparison.OrdinalIgnoreCase) &&
                                         leaf.Contains("Path", StringComparison.OrdinalIgnoreCase),
            AssetCommand.Rename => leaf.Equals("Rename", StringComparison.OrdinalIgnoreCase),
            AssetCommand.Duplicate => leaf.Equals("Duplicate", StringComparison.OrdinalIgnoreCase),
            AssetCommand.Delete => leaf.Equals("Delete", StringComparison.OrdinalIgnoreCase),
            AssetCommand.Reimport => leaf.Equals("Reimport", StringComparison.OrdinalIgnoreCase),
            AssetCommand.Refresh => leaf.Equals("Refresh", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string Describe(AssetCommand command) => command switch
    {
        AssetCommand.ShowInExplorer => "Show in Explorer",
        AssetCommand.CopyPath => "Copy Path",
        AssetCommand.CopyFullPath => "Copy Full Path",
        _ => command.ToString()
    };

    private static bool IsImportPackage(string path) =>
        LeafName(path).TrimEnd('.').Equals("Import Package", StringComparison.OrdinalIgnoreCase);

    private static bool IsExportPackage(string path) =>
        LeafName(path).TrimEnd('.').Equals("Export Package", StringComparison.OrdinalIgnoreCase);

    private static bool IsImportNewAsset(string path) =>
        LeafName(path).TrimEnd('.').Equals("Import New Asset", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<MenuSnapshot> CaptureGenericMenu(Assembly editorAssembly, Action show)
    {
        var dispatcherType = editorAssembly.GetType("BEngine.Editor.GenericMenuDispatcher", throwOnError: true)!;
        var handler = dispatcherType.GetProperty("Handler", StaticMembers) ??
                      throw new MissingMemberException(dispatcherType.FullName, "Handler");
        var previous = handler.GetValue(null);
        try
        {
            _capturedMenu = [];
            handler.SetValue(null, BuildMenuCaptureDelegate(handler.PropertyType));
            show();
            return _capturedMenu;
        }
        finally
        {
            handler.SetValue(null, previous);
        }
    }

    private static Delegate BuildMenuCaptureDelegate(Type delegateType)
    {
        var parameterType = delegateType.GetMethod("Invoke")!.GetParameters().Single().ParameterType;
        var parameter = Expression.Parameter(parameterType, "items");
        var capture = typeof(AssetMenuIntegrationTests).GetMethod(nameof(CaptureMenu), StaticMembers)!;
        return Expression.Lambda(delegateType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile();
    }

    private static void CaptureMenu(object rawItems)
    {
        var items = new List<MenuSnapshot>();
        foreach (var item in (IEnumerable)rawItems)
        {
            if (Read<bool>(item, "Separator")) continue;
            items.Add(new MenuSnapshot(
                Read<string>(item, "Path"),
                Read<bool>(item, "Enabled"),
                Read<Delegate?>(item, "Action")));
        }
        _capturedMenu = items;
    }

    private static T Read<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, InstanceMembers) ??
                       throw new MissingMemberException(source.GetType().FullName, propertyName);
        return (T)property.GetValue(source)!;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, InstanceMembers) ??
                    throw new MissingFieldException(target.GetType().FullName, fieldName);
        field.SetValue(target, value);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

    private static string LeafName(string path)
    {
        var normalized = NormalizePath(path);
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? normalized : normalized[(separator + 1)..];
    }

    private static string DescribeDifference(ISet<string> expected, ISet<string> actual)
    {
        var missing = expected.Except(actual, StringComparer.Ordinal).Order().ToArray();
        var extra = actual.Except(expected, StringComparer.Ordinal).Order().ToArray();
        return $"Missing [{string.Join(", ", missing)}]; extra [{string.Join(", ", extra)}].";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record MenuSnapshot(string Path, bool Enabled, Delegate? Action);
    private readonly record struct CreationExpectation(
        string Method,
        string FileName,
        string ContentFragment);

    private enum AssetCommand
    {
        Open,
        ShowInExplorer,
        CopyPath,
        CopyFullPath,
        Rename,
        Duplicate,
        Delete,
        Reimport,
        Refresh
    }
}
