using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using EditorAssetDatabase = BEngine.Editor.AssetDatabase;
using InspectorEditor = BEngine.Editor.Editor;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.AssemblyDefinitionEditor;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineAsmdefEditor_{Guid.NewGuid():N}");
        object? host = null;
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(root, "Assembly Definition Editor");
            var renamePath = Path.Combine(workspace.ScriptsPath, "RenameTarget.asmdef.yaml");
            new AssemblyDefinitionDocument
            {
                Name = "RenameTarget",
                RootNamespace = "Tests.RenameTarget"
            }.Save(renamePath);

            var projectAssets = new ProjectAssetDatabase(workspace);
            projectAssets.Refresh();
            host = AttachEditorHost(workspace, projectAssets);

            VerifyAssemblyDefinitionAssetLoading();
            VerifyCompoundExtensionRules(workspace);
            VerifyCustomEditorRegistration();
            VerifyCreationUtilityAndMenu(workspace);
            VerifyAssemblyDefinitionValidation();
            VerifyScriptingDefineSymbols(workspace);

            Console.WriteLine(
                "ASSEMBLY_DEFINITION_EDITOR_OK|asset,compound-extension,custom-editor,create-menu,validation,defines");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ASSEMBLY_DEFINITION_EDITOR_FAILED|{exception}");
            return 1;
        }
        finally
        {
            if (host is not null) DetachEditorHost(host);
            TryDelete(root);
        }
    }

    private static void VerifyAssemblyDefinitionAssetLoading()
    {
        var asset = EditorAssetDatabase.LoadMainAssetAtPath("Assets/Scripts/Game.asmdef.yaml");
        Require(asset is AssemblyDefinitionAsset,
            ".asmdef.yaml did not load as AssemblyDefinitionAsset.");
        var definitionAsset = (AssemblyDefinitionAsset)asset!;
        Require(definitionAsset.importError.Length == 0 &&
                definitionAsset.definition.Name == "Game" &&
                definitionAsset.definition.RootNamespace == "Game",
            "AssemblyDefinitionAsset did not expose its parsed document.");
        Require(EditorAssetDatabase.GetMainAssetTypeAtPath("Assets/Scripts/Game.asmdef.yaml") ==
                typeof(AssemblyDefinitionAsset),
            "AssetDatabase reported the wrong main asset type for an assembly definition.");
    }

    private static void VerifyCompoundExtensionRules(ProjectWorkspace workspace)
    {
        var utility = typeof(EditorAssetDatabase).Assembly.GetType(
            "BEngine.Editor.AssetPathUtility", throwOnError: true)!;
        var split = utility.GetMethod("SplitNameAndExtension",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(utility.FullName, "SplitNameAndExtension");
        var result = split.Invoke(null, ["Assets/Scripts/Game.asmdef.yaml"]);
        Require(result is ValueTuple<string, string> { Item1: "Game", Item2: ".asmdef.yaml" },
            "AssetPathUtility did not preserve the compound assembly definition extension.");

        var unique = EditorAssetDatabase.GenerateUniqueAssetPath("Assets/Scripts/Game.asmdef.yaml");
        Require(unique == "Assets/Scripts/Game 1.asmdef.yaml",
            $"GenerateUniqueAssetPath broke the compound extension: {unique}");

        var error = EditorAssetDatabase.RenameAsset("Assets/Scripts/RenameTarget.asmdef.yaml", "Renamed");
        Require(error.Length == 0, $"RenameAsset failed: {error}");
        var renamed = Path.Combine(workspace.ScriptsPath, "Renamed.asmdef.yaml");
        Require(File.Exists(renamed) && File.Exists(renamed + ".meta") &&
                !File.Exists(Path.Combine(workspace.ScriptsPath, "Renamed.yaml")),
            "RenameAsset did not preserve .asmdef.yaml and its metadata sidecar.");
    }

    private static void VerifyCustomEditorRegistration()
    {
        var attribute = typeof(AssemblyDefinitionAssetEditor)
            .GetCustomAttributes<CustomEditorAttribute>(inherit: false)
            .SingleOrDefault(candidate => candidate.inspectedType == typeof(AssemblyDefinitionAsset));
        Require(attribute is not null,
            "AssemblyDefinitionAssetEditor is not registered for AssemblyDefinitionAsset.");

        var asset = EditorAssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(
            "Assets/Scripts/Game.asmdef.yaml") ??
            throw new InvalidOperationException("Assembly definition asset disappeared before editor creation.");
        using var editor = InspectorEditor.CreateEditor(asset);
        Require(editor is AssemblyDefinitionAssetEditor,
            $"Editor registry resolved {editor.GetType().FullName} instead of AssemblyDefinitionAssetEditor.");
    }

    private static void VerifyCreationUtilityAndMenu(ProjectWorkspace workspace)
    {
        var command = typeof(AssemblyDefinitionAssetUtility)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(method => method.GetCustomAttributes<MenuItemAttribute>()
                .Select(attribute => (Method: method, Attribute: attribute)))
            .SingleOrDefault(item =>
                item.Attribute.itemName == "Assets/Create/Assembly Definition" &&
                !item.Attribute.isValidateFunction);
        Require(command.Method is not null,
            "Assets/Create/Assembly Definition is not registered as a MenuItem.");

        var first = AssemblyDefinitionAssetUtility.Create("Assets/Scripts");
        var second = AssemblyDefinitionAssetUtility.Create("Assets/Scripts");
        Require(first == "Assets/Scripts/New Assembly Definition.asmdef.yaml" &&
                second == "Assets/Scripts/New Assembly Definition 1.asmdef.yaml",
            $"Assembly definition creation did not produce stable unique paths: {first}, {second}");
        var firstDocument = Document.Load<AssemblyDefinitionDocument>(
            Path.Combine(workspace.RootPath, first.Replace('/', Path.DirectorySeparatorChar)));
        Require(firstDocument.Name == "New.Assembly.Definition" &&
                firstDocument.RootNamespace == "New.Assembly.Definition",
            "AssemblyDefinitionAssetUtility did not initialize a valid assembly name and root namespace.");
        Require(EditorAssetDatabase.LoadMainAssetAtPath(first) is AssemblyDefinitionAsset,
            "A newly created assembly definition was not imported into AssetDatabase.");
    }

    private static void VerifyAssemblyDefinitionValidation()
    {
        var valid = NewDefinition("Validation.Valid");
        valid.DefineConstraints = ["FEATURE_ENABLED", "!FEATURE_DISABLED"];
        _ = valid.ToYaml();

        var invalidConstraint = NewDefinition("Validation.Constraint");
        invalidConstraint.DefineConstraints = ["FEATURE-INVALID"];
        ExpectInvalid(invalidConstraint, "define constraint", "FEATURE-INVALID");

        var selfReference = NewDefinition("Validation.Self");
        selfReference.References = [selfReference.Name];
        ExpectInvalid(selfReference, "cannot reference itself");

        var platformConflict = NewDefinition("Validation.Platform");
        platformConflict.IncludePlatforms = ["Windows"];
        platformConflict.ExcludePlatforms = ["windows"];
        ExpectInvalid(platformConflict, "both included and excluded");
    }

    private static void VerifyScriptingDefineSymbols(ProjectWorkspace workspace)
    {
        EditorProjectSettings.Initialize(workspace.ProjectSettingsFilePath, workspace.Project.Name);
        EditorProjectSettings.current.ScriptingDefineSymbols = ["FEATURE_ALPHA", "FEATURE_2"];
        EditorProjectSettings.Save();

        var saved = Document.Load<ProjectSettingsDocument>(workspace.ProjectSettingsFilePath);
        Require(saved.ScriptingDefineSymbols.SequenceEqual(["FEATURE_ALPHA", "FEATURE_2"]),
            "Project Settings did not persist scripting define symbols.");

        EditorProjectSettings.current.ScriptingDefineSymbols = ["INVALID-SYMBOL"];
        ExpectInvalidOperation(EditorProjectSettings.Save, "Invalid scripting define symbol");
    }

    private static AssemblyDefinitionDocument NewDefinition(string name) => new()
    {
        Name = name,
        RootNamespace = name
    };

    private static void ExpectInvalid(AssemblyDefinitionDocument document, params string[] fragments)
    {
        try
        {
            _ = document.ToYaml();
        }
        catch (InvalidDataException exception)
        {
            foreach (var fragment in fragments)
                Require(exception.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"Validation error did not identify '{fragment}': {exception.Message}");
            return;
        }
        throw new InvalidOperationException($"Invalid assembly definition '{document.Name}' was accepted.");
    }

    private static void ExpectInvalidOperation(Action action, string fragment)
    {
        try
        {
            action();
        }
        catch (InvalidDataException exception)
        {
            Require(exception.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"Project settings validation did not identify '{fragment}': {exception.Message}");
            return;
        }
        throw new InvalidOperationException("Invalid project scripting define symbols were accepted.");
    }

    private static object AttachEditorHost(ProjectWorkspace workspace, ProjectAssetDatabase assets)
    {
        var editorAssembly = typeof(InspectorEditor).Assembly;
        var applicationType = editorAssembly.GetType(
            "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(applicationType);
        SetField(host, "_workspace", workspace);
        SetField(host, "_assets", assets);

        var projectWindowType = applicationType.GetNestedType(
            "ImGuiProjectWindow", BindingFlags.NonPublic) ??
            throw new TypeLoadException("GpuEditorApplication.ImGuiProjectWindow was not found.");
        var projectWindow = Activator.CreateInstance(projectWindowType, nonPublic: true) ??
            throw new InvalidOperationException("A lightweight Project window could not be created.");
        SetField(host, "_project", projectWindow);

        var bridge = editorAssembly.GetType("BEngine.Editor.EditorBridge", throwOnError: true)!;
        bridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
        return host;
    }

    private static void DetachEditorHost(object host)
    {
        var bridge = typeof(InspectorEditor).Assembly.GetType("BEngine.Editor.EditorBridge", throwOnError: true)!;
        bridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(target.GetType().FullName, fieldName);
        field.SetValue(target, value);
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
}
