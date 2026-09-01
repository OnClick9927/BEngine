using System.Reflection;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.PackageSourceCompilation;

internal static class Program
{
    private static int Main()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"BEnginePackageSources_{Guid.NewGuid():N}");
        var previousRepository = Environment.GetEnvironmentVariable("BENGINE_PACKAGES_PATH");
        try
        {
            VerifyDistributedPackages();
            VerifyDistributedPackageCompilation(temporaryRoot);
            var repository = Path.Combine(temporaryRoot, "Output", "Packages");
            var fixture = new SourcePackageFixture(repository);
            fixture.Create();
            Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", repository);

            var workspace = ProjectWorkspaceFactory.Create(
                Path.Combine(temporaryRoot, "Project"),
                "Package Source Compilation",
                [SourcePackageFixture.ConsumerPackageId]);
            var repositoryCatalog = new BPackageCatalog(Directory.EnumerateFiles(
                repository, "package.yaml", SearchOption.AllDirectories));
            var catalog = new BPackageCatalog(Directory.EnumerateFiles(
                workspace.PackagesPath, "package.yaml", SearchOption.AllDirectories));
            var enabled = catalog.packages.Where(definition =>
                definition.Document.Id is SourcePackageFixture.BasePackageId or
                    SourcePackageFixture.ConsumerPackageId).ToArray();

            VerifySourceOnlyPackageTree(repository, repositoryCatalog.packages);
            VerifySourceOnlyPackageTree(workspace.PackagesPath, enabled);

            var first = PackageSourceCompilerAdapter.CompileEnabled(workspace, enabled);
            VerifyCompilationOutputs(workspace, first);
            VerifyCrossPackageLoad(first, "consumer:base-v1:editor:base-v1");
            var firstPaths = first.ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var unchanged = PackageSourceCompilerAdapter.CompileEnabled(workspace, enabled);
            TestAssert.That(firstPaths.All(pair => unchanged.TryGetValue(pair.Key, out var path) &&
                                                     PathsEqual(pair.Value, path)),
                "Unchanged package sources did not reuse their incremental assembly artifacts.");

            File.WriteAllText(fixture.CachedBaseSource(workspace.PackagesPath), """
                namespace BEngine.Tests.SourceBase;

                public static class BaseValue
                {
                    public static string Get() => "base-v2";
                }
                """);
            var second = PackageSourceCompilerAdapter.CompileEnabled(workspace, enabled);
            VerifyCompilationOutputs(workspace, second);
            VerifyCrossPackageLoad(second, "consumer:base-v2:editor:base-v2");
            TestAssert.That(second.Any(pair => firstPaths.TryGetValue(pair.Key, out var firstPath) &&
                                               !PathsEqual(firstPath, pair.Value)),
                "Changing package source did not publish a new package assembly artifact.");
            VerifyMissingAssemblyDefinitionRejected(workspace, enabled, fixture);

            using (var manager = CreateManifestOnlyManager(workspace, catalog))
            {
                manager.SetEnabled(SourcePackageFixture.ConsumerPackageId, false);
                manager.SetEnabled(SourcePackageFixture.BasePackageId, false);
            }
            TestAssert.That(!Directory.Exists(fixture.CachedPackageRoot(
                    workspace.PackagesPath, SourcePackageFixture.ConsumerPackageId)),
                "Disabling the consumer package left its project source cache behind.");
            TestAssert.That(!Directory.Exists(fixture.CachedPackageRoot(
                    workspace.PackagesPath, SourcePackageFixture.BasePackageId)),
                "Disabling the base package left its project source cache behind.");

            Console.WriteLine(
                "PACKAGE_SOURCE_COMPILATION_OK|source-distribution,assembly-definitions,strict-validation,incremental-reuse,project-packages,runtime-editor-dependencies,versioned-output,recompile,disable-delete");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PACKAGE_SOURCE_COMPILATION_FAILED|{exception}");
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", previousRepository);
            if (Environment.GetEnvironmentVariable("BENGINE_KEEP_TEST_TEMP") == "1")
                Console.WriteLine($"PACKAGE_SOURCE_COMPILATION_TEMP|{temporaryRoot}");
            else if (Directory.Exists(temporaryRoot))
            {
                try { Directory.Delete(temporaryRoot, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void VerifyDistributedPackages()
    {
        var root = FindRepositoryRoot();
        var packagesRoot = Path.Combine(root, "Output", "Packages");
        var catalog = new BPackageCatalog(Directory.EnumerateFiles(
            packagesRoot, "package.yaml", SearchOption.AllDirectories));
        TestAssert.That(catalog.packages.Count > 0, "Output/Packages contains no package definitions.");
        TestAssert.That(catalog.packages.All(definition => !definition.Document.Id.Equals(
                "com.bengine.codex", StringComparison.OrdinalIgnoreCase)),
            "Codex is still distributed as a source package.");
        VerifySourceOnlyPackageTree(packagesRoot, catalog.packages);
    }

    private static void VerifyDistributedPackageCompilation(string temporaryRoot)
    {
        var root = FindRepositoryRoot();
        var repositoryCatalog = new BPackageCatalog(Directory.EnumerateFiles(
            Path.Combine(root, "Output", "Packages"), "package.yaml", SearchOption.AllDirectories));
        var workspace = ProjectWorkspaceFactory.Create(
            Path.Combine(temporaryRoot, "DistributedProject"),
            "Distributed Package Compilation",
            repositoryCatalog.packages.Select(definition => definition.Document.Id));
        var projectCatalog = new BPackageCatalog(Directory.EnumerateFiles(
            workspace.PackagesPath, "package.yaml", SearchOption.AllDirectories));
        var paths = PackageSourceCompilerAdapter.CompileEnabled(workspace, projectCatalog.packages);
        var expectedAssemblies = projectCatalog.packages.SelectMany(definition => new[]
            {
                definition.Document.Runtime?.Assembly,
                definition.Document.Editor?.Assembly
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
        TestAssert.That(expectedAssemblies.All(name => paths.TryGetValue(name, out var path) && File.Exists(path)),
            "At least one distributed source package assembly was not compiled by the project.");

        var uiEditor = paths["BEngine.UIElements.Editor"];
        var uiOutput = Path.GetDirectoryName(uiEditor)!;
        TestAssert.That(File.Exists(Path.Combine(uiOutput, "AngleSharp.dll")) &&
                        File.Exists(Path.Combine(uiOutput, "AngleSharp.Css.dll")),
            "UIElements external package dependencies were not copied beside its project-built editor assembly. " +
            $"Files: {string.Join(", ", Directory.EnumerateFiles(uiOutput).Select(Path.GetFileName))}");
    }

    private static void VerifySourceOnlyPackageTree(
        string root,
        IEnumerable<BPackageDefinition> definitions)
    {
        var definitionArray = definitions.ToArray();
        var definitionsById = definitionArray.ToDictionary(
            definition => definition.Document.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitionArray)
        {
            var packageRoot = Path.GetDirectoryName(Path.GetFullPath(definition.Path))!;
            TestAssert.That(IsInside(root, packageRoot),
                $"Package '{definition.Document.Id}' is outside '{root}'.");
            foreach (var entry in new[]
                     {
                         (Kind: "runtime", Assembly: definition.Document.Runtime),
                         (Kind: "editor", Assembly: definition.Document.Editor)
                     }.Where(entry => entry.Assembly is not null))
            {
                var assembly = entry.Assembly!;
                var sourceRoot = Path.Combine(packageRoot, assembly!.Assembly);
                TestAssert.That(Directory.Exists(sourceRoot) &&
                                Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Any(),
                    $"Package '{definition.Document.Id}' has no source for '{assembly.Assembly}'.");
                var expectedDefinitionPath = Path.Combine(sourceRoot, $"{assembly.Assembly}.asmdef.yaml");
                var assemblyDefinitionFiles = Directory.EnumerateFiles(
                    sourceRoot, "*.asmdef.yaml", SearchOption.TopDirectoryOnly).ToArray();
                TestAssert.That(assemblyDefinitionFiles.Length == 1 &&
                                PathsEqual(assemblyDefinitionFiles[0], expectedDefinitionPath),
                    $"Package assembly '{assembly.Assembly}' does not contain exactly one matching asmdef.");
                var assemblyDefinition = BEngine.YamlUtility.Load<AssemblyDefinitionDocument>(expectedDefinitionPath);
                TestAssert.That(assemblyDefinition.Name == assembly.Assembly &&
                                assemblyDefinition.RootNamespace == assembly.RootNamespace &&
                                assemblyDefinition.EditorOnly == (entry.Kind == "editor"),
                    $"Package assembly definition '{expectedDefinitionPath}' disagrees with package.yaml.");
                var expectedReferences = assembly.Dependencies
                    .Where(dependency => definitionsById.ContainsKey(dependency.PackageId))
                    .Select(dependency => dependency.Target == "editor"
                        ? definitionsById[dependency.PackageId].Document.Editor?.Assembly
                        : definitionsById[dependency.PackageId].Document.Runtime?.Assembly)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (entry.Kind == "editor" && definition.Document.Runtime is { } ownRuntime)
                    expectedReferences.Add(ownRuntime.Assembly);
                TestAssert.That(expectedReferences.SetEquals(assemblyDefinition.References),
                    $"Package assembly definition '{expectedDefinitionPath}' has stale references.");
                foreach (var suffix in new[] { ".dll", ".pdb", ".deps.json" })
                    TestAssert.That(!Directory.EnumerateFiles(packageRoot,
                            $"{assembly.Assembly}{suffix}", SearchOption.AllDirectories).Any(),
                        $"Package '{definition.Document.Id}' contains precompiled artifact " +
                        $"'{assembly.Assembly}{suffix}'.");
            }
            TestAssert.That(!Directory.EnumerateDirectories(packageRoot, "bin", SearchOption.AllDirectories).Any(),
                $"Package '{definition.Document.Id}' contains a bin directory.");
            TestAssert.That(!Directory.EnumerateDirectories(packageRoot, "obj", SearchOption.AllDirectories).Any(),
                $"Package '{definition.Document.Id}' contains an obj directory.");
        }
    }

    private static void VerifyMissingAssemblyDefinitionRejected(
        ProjectWorkspace workspace,
        IReadOnlyCollection<BPackageDefinition> definitions,
        SourcePackageFixture fixture)
    {
        var definitionPath = Path.Combine(
            fixture.CachedPackageRoot(workspace.PackagesPath, SourcePackageFixture.ConsumerPackageId),
            SourcePackageFixture.ConsumerEditorAssembly,
            $"{SourcePackageFixture.ConsumerEditorAssembly}.asmdef.yaml");
        var yaml = File.ReadAllText(definitionPath);
        File.Delete(definitionPath);
        try
        {
            Exception? failure = null;
            try { PackageSourceCompilerAdapter.CompileEnabled(workspace, definitions); }
            catch (Exception exception) { failure = exception; }
            TestAssert.That(failure is not null && ExceptionText(failure).Contains(
                    "assembly definition", StringComparison.OrdinalIgnoreCase),
                "Package compilation accepted an assembly directory without its required asmdef.");
        }
        finally
        {
            File.WriteAllText(definitionPath, yaml);
        }
    }

    private static string ExceptionText(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException!)
            messages.Add(current.Message);
        return string.Join(Environment.NewLine, messages);
    }

    private static void VerifyCompilationOutputs(
        ProjectWorkspace workspace,
        IReadOnlyDictionary<string, string> paths)
    {
        foreach (var assemblyName in new[]
                 {
                     SourcePackageFixture.BaseRuntimeAssembly,
                     SourcePackageFixture.BaseEditorAssembly,
                     SourcePackageFixture.ConsumerRuntimeAssembly,
                     SourcePackageFixture.ConsumerEditorAssembly
                 })
        {
            TestAssert.That(paths.TryGetValue(assemblyName, out var path) && File.Exists(path),
                $"Package source compiler did not produce '{assemblyName}'.");
            TestAssert.That(IsInside(workspace.ScriptAssembliesPath, path!),
                $"'{assemblyName}' was not published inside Library/ScriptAssemblies.");
            var current = ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName);
            TestAssert.That(current is not null && PathsEqual(current, path!),
                $"'{assemblyName}' current assembly reference does not point at the compiled artifact.");
        }
    }

    private static void VerifyCrossPackageLoad(
        IReadOnlyDictionary<string, string> paths,
        string expected)
    {
        var context = new PackageAssemblyLoadContext(paths);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(
                paths[SourcePackageFixture.ConsumerEditorAssembly]));
            var type = assembly.GetType("BEngine.Tests.SourceConsumer.Editor.ConsumerEditorValue", true)!;
            var actual = type.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null) as string;
            TestAssert.That(actual == expected,
                $"Compiled package dependency returned '{actual}', expected '{expected}'.");
        }
        finally
        {
            context.Unload();
        }
    }

    private static BPackageManager CreateManifestOnlyManager(
        ProjectWorkspace workspace,
        BPackageCatalog catalog)
    {
        var constructor = typeof(BPackageManager).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(ProjectWorkspace), typeof(BPackageCatalog), typeof(bool)],
            modifiers: null) ?? throw new MissingMethodException(
            typeof(BPackageManager).FullName, ".ctor(ProjectWorkspace, BPackageCatalog, bool)");
        return (BPackageManager)constructor.Invoke([workspace, catalog, false]);
    }

    private static bool IsInside(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) +
                             Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
    }
}
