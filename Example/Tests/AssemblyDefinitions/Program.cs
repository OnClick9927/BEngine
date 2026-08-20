using System.Reflection;
using System.Runtime.Loader;
using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.AssemblyDefinitions;

internal static class Program
{
    private static int Main()
    {
        var roots = new List<string>();
        try
        {
            VerifyDiscoveryAndNearestOwnership(roots);
            VerifyRuntimeTopologyAndEditorOnlyAssembly(roots);
            VerifyIncrementalDependencyRebuild(roots);
            VerifyDefineConstraintsAndUnsafeCode(roots);
            VerifyPlatformFilters(roots);
            VerifyAutoReferencedFallbackAssembly(roots);
            VerifyDuplicateNameFailure(roots);
            VerifyMissingReferenceFailure(roots);
            VerifyRuntimeCannotReferenceEditorAssembly(roots);
            VerifyCycleFailure(roots);

            Console.WriteLine(
                "ASSEMBLY_DEFINITIONS_OK|discovery,nearest-owner,topology,incremental-dependents," +
                "define-constraints,unsafe," +
                "platform-filters,auto-reference,duplicate-name,missing-reference,runtime-editor-boundary," +
                "cycle,editor-only");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ASSEMBLY_DEFINITIONS_FAILED|{exception}");
            return 1;
        }
        finally
        {
            foreach (var root in roots) TryDelete(root);
        }
    }

    private static void VerifyDiscoveryAndNearestOwnership(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "Discovery");
        var outerDirectory = Path.Combine(workspace.ScriptsPath, "Outer");
        var innerDirectory = Path.Combine(outerDirectory, "Inner");
        WriteDefinition(outerDirectory, "Discovery.Outer");
        WriteDefinition(innerDirectory, "Discovery.Inner");
        var innerSource = WriteSource(innerDirectory, "InnerType.cs", "namespace Discovery.Inner; public class InnerType { }");

        var database = new ProjectAssemblyDatabase(workspace);
        var definitions = database.Discover();
        Require(definitions.Select(item => item.Document.Name).SequenceEqual(
                ["Discovery.Outer", "Discovery.Inner"]),
            "Assembly definitions must be discovered deterministically by path.");
        Require(database.FindForSource(innerSource)?.Document.Name == "Discovery.Inner",
            "The closest enclosing assembly definition must own a nested source file.");
    }

    private static void VerifyRuntimeTopologyAndEditorOnlyAssembly(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "Topology");
        var suffix = Guid.NewGuid().ToString("N");
        var foundationName = $"Asmdef.Foundation.{suffix}";
        var gameplayName = $"Asmdef.Gameplay.{suffix}";
        var editorName = $"Asmdef.EditorTools.{suffix}";

        var foundationDirectory = Path.Combine(workspace.ScriptsPath, "Foundation");
        WriteDefinition(foundationDirectory, foundationName, rootNamespace: "AssemblyTests.Foundation");
        WriteSource(foundationDirectory, "SharedValue.cs", """
            namespace AssemblyTests.Foundation;
            public static class SharedValue
            {
                public static int Value => 41;
            }
            """);

        var gameplayDirectory = Path.Combine(workspace.ScriptsPath, "Gameplay");
        WriteDefinition(gameplayDirectory, gameplayName, [foundationName], "AssemblyTests.Gameplay");
        WriteSource(gameplayDirectory, "GameplayProbe.cs", """
            using AssemblyTests.Foundation;
            namespace AssemblyTests.Gameplay;
            public static class GameplayProbe
            {
                public static int Read() => SharedValue.Value + 1;
            }
            """);

        var editorDirectory = Path.Combine(workspace.EditorScriptsPath, "Tools");
        WriteDefinition(editorDirectory, editorName, [gameplayName], "AssemblyTests.Editor", editorOnly: true);
        WriteSource(editorDirectory, "EditorProbe.cs", """
            using AssemblyTests.Gameplay;
            namespace AssemblyTests.Editor;
            public static class EditorProbe
            {
                public static int Read() => GameplayProbe.Read() + 1;
            }
            """);

        var runtimeEntry = ProjectScriptCompiler.CompileAndLoad(workspace) ??
                           throw new InvalidOperationException("The runtime assembly graph produced no entry assembly.");
        var foundationPath = RequireAssembly(workspace, foundationName);
        var gameplayPath = RequireAssembly(workspace, gameplayName);
        Require(ScriptAssemblyStore.ResolveCurrentPath(workspace, editorName) is null,
            "Runtime compilation must not emit an editor-only assembly.");
        Require(EvaluateStaticInt(
                [foundationPath, gameplayPath], gameplayName, "AssemblyTests.Gameplay.GameplayProbe", "Read") == 42,
            "The dependent runtime assembly did not link against its declared assembly reference.");

        _ = EditorProjectScriptCompiler.CompileAndLoad(
                workspace, runtimeEntry, typeof(BEngine.Editor.EditorWindow).Assembly.Location) ??
            throw new InvalidOperationException("The editor assembly graph produced no entry assembly.");
        var editorPath = RequireAssembly(workspace, editorName);
        Require(EvaluateStaticInt(
                [foundationPath, gameplayPath, editorPath], editorName, "AssemblyTests.Editor.EditorProbe", "Read") == 43,
            "The editor-only assembly did not link against its runtime dependency graph.");
    }

    private static void VerifyMissingReferenceFailure(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "MissingReference");
        var suffix = Guid.NewGuid().ToString("N");
        var assemblyName = $"Asmdef.Missing.{suffix}";
        var missingName = $"Asmdef.DoesNotExist.{suffix}";
        var directory = Path.Combine(workspace.ScriptsPath, "Missing");
        WriteDefinition(directory, assemblyName, [missingName]);
        WriteSource(directory, "MissingProbe.cs", "namespace AssemblyTests.Missing; public class MissingProbe { }");

        ExpectFailure(
            () => ProjectScriptCompiler.CompileAndLoad(workspace),
            "Missing assembly reference", assemblyName, missingName);
    }

    private static void VerifyIncrementalDependencyRebuild(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "IncrementalDependencies");
        var suffix = Guid.NewGuid().ToString("N");
        var foundationName = $"Asmdef.Incremental.Foundation.{suffix}";
        var dependentName = $"Asmdef.Incremental.Dependent.{suffix}";
        var unrelatedName = $"Asmdef.Incremental.Unrelated.{suffix}";
        var foundationDirectory = Path.Combine(workspace.ScriptsPath, "Foundation");
        var foundationSource = WriteSource(foundationDirectory, "Foundation.cs",
            "namespace Incremental; public static class Foundation { public static int Value => 1; }");
        WriteDefinition(foundationDirectory, foundationName, rootNamespace: "Incremental");
        var dependentDirectory = Path.Combine(workspace.ScriptsPath, "Dependent");
        WriteDefinition(dependentDirectory, dependentName, [foundationName], "Incremental");
        WriteSource(dependentDirectory, "Dependent.cs",
            "namespace Incremental; public static class Dependent { public static int Value => Foundation.Value; }");
        var unrelatedDirectory = Path.Combine(workspace.ScriptsPath, "Unrelated");
        WriteDefinition(unrelatedDirectory, unrelatedName, rootNamespace: "Incremental.Unrelated");
        WriteSource(unrelatedDirectory, "Unrelated.cs",
            "namespace Incremental.Unrelated; public static class Unrelated { public static int Value => 9; }");

        _ = ProjectScriptCompiler.CompileAndLoad(workspace);
        var firstFoundation = RequireAssembly(workspace, foundationName);
        var firstDependent = RequireAssembly(workspace, dependentName);
        var firstUnrelated = RequireAssembly(workspace, unrelatedName);

        File.WriteAllText(foundationSource,
            "namespace Incremental; public static class Foundation { public static int Value => 2; }");
        _ = ProjectScriptCompiler.CompileAndLoad(workspace);
        Require(RequireAssembly(workspace, foundationName) != firstFoundation,
            "The changed assembly reused its previous build artifact.");
        Require(RequireAssembly(workspace, dependentName) != firstDependent,
            "An assembly depending on a changed assembly was not rebuilt.");
        Require(RequireAssembly(workspace, unrelatedName) == firstUnrelated,
            "An unrelated assembly was rebuilt after another assembly changed.");
    }

    private static void VerifyDefineConstraintsAndUnsafeCode(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "DefinesAndUnsafe");
        var settings = Document.Load<ProjectSettingsDocument>(workspace.ProjectSettingsFilePath);
        settings.ScriptingDefineSymbols = ["ASSEMBLY_TEST_FEATURE"];
        settings.Save(workspace.ProjectSettingsFilePath);

        var suffix = Guid.NewGuid().ToString("N");
        var activeName = $"Asmdef.Defines.Active.{suffix}";
        var excludedName = $"Asmdef.Defines.Excluded.{suffix}";
        var activeDirectory = Path.Combine(workspace.ScriptsPath, "Active");
        WriteDefinition(
            activeDirectory,
            activeName,
            rootNamespace: "AssemblyTests.Defines",
            defineConstraints: ["ASSEMBLY_TEST_FEATURE", "!ASSEMBLY_TEST_DISABLED"],
            allowUnsafeCode: true);
        WriteSource(activeDirectory, "UnsafeDefineProbe.cs", """
            namespace AssemblyTests.Defines;
            public static class UnsafeDefineProbe
            {
            #if ASSEMBLY_TEST_FEATURE
                public static unsafe int Read()
                {
                    int value = 17;
                    int* pointer = &value;
                    return *pointer;
                }
            #else
            #error ASSEMBLY_TEST_FEATURE was not passed to the project assembly compiler.
            #endif
            }
            """);

        var excludedDirectory = Path.Combine(workspace.ScriptsPath, "Excluded");
        WriteDefinition(
            excludedDirectory,
            excludedName,
            defineConstraints: ["!ASSEMBLY_TEST_FEATURE"]);
        WriteSource(excludedDirectory, "ExcludedProbe.cs",
            "namespace AssemblyTests.Defines; public class ExcludedProbe { }");

        _ = ProjectScriptCompiler.CompileAndLoad(workspace) ??
            throw new InvalidOperationException("The constrained assembly graph produced no entry assembly.");
        var activePath = RequireAssembly(workspace, activeName);
        Require(ScriptAssemblyStore.ResolveCurrentPath(workspace, excludedName) is null,
            "An assembly whose negated define constraint failed must not be published.");
        Require(EvaluateStaticInt(
                [activePath], activeName, "AssemblyTests.Defines.UnsafeDefineProbe", "Read") == 17,
            "Define constants or AllowUnsafeCode were not applied to the generated assembly project.");
    }

    private static void VerifyPlatformFilters(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "Platforms");
        var suffix = Guid.NewGuid().ToString("N");
        var includedName = $"Asmdef.Platform.Included.{suffix}";
        var excludedName = $"Asmdef.Platform.Excluded.{suffix}";
        var includedDirectory = Path.Combine(workspace.ScriptsPath, "IncludedPlayer");
        var excludedDirectory = Path.Combine(workspace.ScriptsPath, "ExcludedPlayer");
        WriteDefinition(includedDirectory, includedName, includePlatforms: ["Player"]);
        WriteDefinition(excludedDirectory, excludedName, excludePlatforms: ["Player"]);
        WriteSource(includedDirectory, "IncludedProbe.cs",
            "namespace AssemblyTests.Platform; public class IncludedProbe { }");
        WriteSource(excludedDirectory, "ExcludedProbe.cs",
            "namespace AssemblyTests.Platform; public class ExcludedProbe { }");

        _ = ProjectScriptCompiler.CompileAndLoad(workspace) ??
            throw new InvalidOperationException("The platform-filtered assembly graph produced no entry assembly.");
        _ = RequireAssembly(workspace, includedName);
        Require(ScriptAssemblyStore.ResolveCurrentPath(workspace, excludedName) is null,
            "ExcludePlatforms must prevent a matching player assembly from being published.");
    }

    private static void VerifyAutoReferencedFallbackAssembly(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "AutoReferenced");
        var suffix = Guid.NewGuid().ToString("N");
        var automaticName = $"Asmdef.Automatic.{suffix}";
        var manualName = $"Asmdef.Manual.{suffix}";
        var automaticDirectory = Path.Combine(workspace.ScriptsPath, "Automatic");
        var manualDirectory = Path.Combine(workspace.ScriptsPath, "Manual");
        WriteDefinition(
            automaticDirectory, automaticName, rootNamespace: "AssemblyTests.Automatic", autoReferenced: true);
        WriteDefinition(manualDirectory, manualName, autoReferenced: false);
        WriteSource(automaticDirectory, "AutomaticProbe.cs", """
            namespace AssemblyTests.Automatic;
            public static class AutomaticProbe
            {
                public static int Value => 29;
            }
            """);
        WriteSource(manualDirectory, "ManualProbe.cs",
            "namespace AssemblyTests.Manual; public class ManualProbe { }");
        WriteSource(workspace.ScriptsPath, "FallbackProbe.cs", """
            using AssemblyTests.Automatic;
            namespace Game;
            public static class FallbackProbe
            {
                public static int Read() => AutomaticProbe.Value;
            }
            """);

        _ = ProjectScriptCompiler.CompileAndLoad(workspace) ??
            throw new InvalidOperationException("The fallback script assembly was not compiled.");
        var automaticPath = RequireAssembly(workspace, automaticName);
        _ = RequireAssembly(workspace, manualName);
        var fallbackPath = RequireAssembly(workspace, "GameScripts");
        var manifest = ScriptAssemblyStore.LoadProjectManifest(workspace) ??
                       throw new InvalidOperationException("The runtime project assembly manifest was not published.");
        var fallback = manifest.Assemblies.SingleOrDefault(assembly => assembly.Assembly == "GameScripts") ??
                       throw new InvalidOperationException("GameScripts is missing from the project assembly manifest.");
        Require(fallback.References.Contains(automaticName, StringComparer.OrdinalIgnoreCase),
            "GameScripts must reference an AutoReferenced assembly definition.");
        Require(!fallback.References.Contains(manualName, StringComparer.OrdinalIgnoreCase),
            "GameScripts must not reference an assembly definition with AutoReferenced disabled.");
        Require(EvaluateStaticInt(
                [automaticPath, fallbackPath], "GameScripts", "Game.FallbackProbe", "Read") == 29,
            "The fallback GameScripts assembly did not link against its automatic assembly reference.");
    }

    private static void VerifyRuntimeCannotReferenceEditorAssembly(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "RuntimeEditorBoundary");
        var suffix = Guid.NewGuid().ToString("N");
        var runtimeName = $"Asmdef.RuntimeBoundary.{suffix}";
        var editorName = $"Asmdef.EditorBoundary.{suffix}";
        var runtimeDirectory = Path.Combine(workspace.ScriptsPath, "Runtime");
        var editorDirectory = Path.Combine(workspace.EditorScriptsPath, "EditorOnly");
        WriteDefinition(runtimeDirectory, runtimeName, [editorName]);
        WriteDefinition(editorDirectory, editorName, editorOnly: true);
        WriteSource(runtimeDirectory, "RuntimeBoundary.cs",
            "namespace AssemblyTests.Boundary; public class RuntimeBoundary { }");
        WriteSource(editorDirectory, "EditorBoundary.cs",
            "namespace AssemblyTests.Boundary; public class EditorBoundary { }");

        ExpectFailure(
            () => ProjectScriptCompiler.CompileAndLoad(workspace),
            "editor-only", runtimeName, editorName);
    }

    private static void VerifyDuplicateNameFailure(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "DuplicateName");
        var assemblyName = $"Asmdef.Duplicate.{Guid.NewGuid():N}";
        var firstDirectory = Path.Combine(workspace.ScriptsPath, "First");
        var secondDirectory = Path.Combine(workspace.ScriptsPath, "Second");
        WriteDefinition(firstDirectory, assemblyName);
        WriteDefinition(secondDirectory, assemblyName);
        WriteSource(firstDirectory, "First.cs", "namespace AssemblyTests.Duplicate; public class First { }");
        WriteSource(secondDirectory, "Second.cs", "namespace AssemblyTests.Duplicate; public class Second { }");

        ExpectFailure(
            () => ProjectScriptCompiler.CompileAndLoad(workspace),
            "duplicate", assemblyName);
    }

    private static void VerifyCycleFailure(ICollection<string> roots)
    {
        var workspace = CreateWorkspace(roots, "Cycle");
        var suffix = Guid.NewGuid().ToString("N");
        var firstName = $"Asmdef.CycleA.{suffix}";
        var secondName = $"Asmdef.CycleB.{suffix}";
        var firstDirectory = Path.Combine(workspace.ScriptsPath, "CycleA");
        var secondDirectory = Path.Combine(workspace.ScriptsPath, "CycleB");
        WriteDefinition(firstDirectory, firstName, [secondName]);
        WriteDefinition(secondDirectory, secondName, [firstName]);
        WriteSource(firstDirectory, "CycleA.cs", "namespace AssemblyTests.Cycle; public class CycleA { }");
        WriteSource(secondDirectory, "CycleB.cs", "namespace AssemblyTests.Cycle; public class CycleB { }");

        ExpectFailure(
            () => ProjectScriptCompiler.CompileAndLoad(workspace),
            "cycle", firstName, secondName);
    }

    private static ProjectWorkspace CreateWorkspace(ICollection<string> roots, string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineAsmdef_{scenario}_{Guid.NewGuid():N}");
        roots.Add(root);
        var workspace = ProjectWorkspaceFactory.Create(root, $"Assembly Definition {scenario}");
        Directory.Delete(workspace.AssetsPath, recursive: true);
        Directory.CreateDirectory(workspace.ScriptsPath);
        Directory.CreateDirectory(workspace.EditorScriptsPath);
        return workspace;
    }

    private static void WriteDefinition(
        string directory,
        string name,
        IEnumerable<string>? references = null,
        string? rootNamespace = null,
        bool editorOnly = false,
        IEnumerable<string>? defineConstraints = null,
        IEnumerable<string>? includePlatforms = null,
        IEnumerable<string>? excludePlatforms = null,
        bool autoReferenced = true,
        bool allowUnsafeCode = false)
    {
        Directory.CreateDirectory(directory);
        new AssemblyDefinitionDocument
        {
            Name = name,
            RootNamespace = rootNamespace ?? name.Replace('.', '_'),
            References = references?.ToList() ?? [],
            EditorOnly = editorOnly,
            DefineConstraints = defineConstraints?.ToList() ?? [],
            IncludePlatforms = includePlatforms?.ToList() ?? [],
            ExcludePlatforms = excludePlatforms?.ToList() ?? [],
            AutoReferenced = autoReferenced,
            AllowUnsafeCode = allowUnsafeCode
        }.Save(Path.Combine(directory, $"{name}.asmdef.yaml"));
    }

    private static string WriteSource(string directory, string fileName, string source)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, source);
        return path;
    }

    private static string RequireAssembly(ProjectWorkspace workspace, string assemblyName)
    {
        var path = ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName);
        if (path is null || !File.Exists(path))
            throw new InvalidOperationException($"Assembly '{assemblyName}' was not published.");
        Require(AssemblyName.GetAssemblyName(path).Name == assemblyName,
            $"Published output for '{assemblyName}' has the wrong assembly identity.");
        return path;
    }

    private static int EvaluateStaticInt(
        IReadOnlyList<string> assemblyPaths,
        string entryAssemblyName,
        string typeName,
        string methodName)
    {
        var paths = assemblyPaths.ToDictionary(
            path => AssemblyName.GetAssemblyName(path).Name!,
            Path.GetFullPath,
            StringComparer.OrdinalIgnoreCase);
        var context = new AssemblyLoadContext($"BEngine.AsmdefTest:{Guid.NewGuid():N}", isCollectible: true);
        context.Resolving += (_, name) => name.Name is { } simpleName && paths.TryGetValue(simpleName, out var path)
            ? context.LoadFromAssemblyPath(path)
            : null;
        try
        {
            var assembly = context.LoadFromAssemblyPath(paths[entryAssemblyName]);
            var method = assembly.GetType(typeName, throwOnError: true)!
                .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static) ??
                throw new MissingMethodException(typeName, methodName);
            return (int)(method.Invoke(null, null) ?? throw new InvalidOperationException("Probe returned null."));
        }
        finally
        {
            context.Unload();
        }
    }

    private static void ExpectFailure(Action action, params string[] expectedFragments)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            var details = exception.ToString();
            foreach (var fragment in expectedFragments)
                Require(details.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"Failure did not identify '{fragment}': {details}");
            return;
        }

        throw new InvalidOperationException(
            $"Expected failure containing: {string.Join(", ", expectedFragments)}");
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
