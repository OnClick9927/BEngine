using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.ProjectSystem;

internal static class PackageSourceCompiler
{
    internal static PackageCompilationResult CompileEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions,
        Action<int, int, string>? progress = null)
        => CompileEnabled(workspace, definitions, ProcessUtility.Current, progress);

    internal static PackageCompilationResult CompileEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions,
        ProcessUtility processRunner,
        Action<int, int, string>? progress = null,
        bool isolated = false,
        ScriptBuildConfiguration? playerConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(processRunner);

        var nodes = CreateNodes(definitions)
            .Where(node => playerConfiguration is null || node.Kind == "runtime")
            .ToDictionary(node => node.Key, StringComparer.OrdinalIgnoreCase);
        ValidateAssemblyDefinitions(nodes.Values);
        var remaining = new Dictionary<string, CompilationNode>(nodes, StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(node => Dependencies(node, nodes).All(dependency => completed.Contains(dependency.Key)))
                .OrderBy(node => node.Definition.Document.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Kind, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
                throw new InvalidDataException("Package source dependency cycle prevents compilation.");

            foreach (var node in ready)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EditorCallbackDispatcher.Invoke(progress, index, nodes.Count,
                    $"{node.Definition.Document.DisplayName} ({node.Kind})",
                    "PackageSourceCompiler.progress");
                var references = CreateReferences(node, nodes, paths);
                var assemblyPath = CompileNode(workspace, node, references, processRunner, isolated, playerConfiguration);
                paths[node.Assembly.Assembly] = assemblyPath;
                completed.Add(node.Key);
                remaining.Remove(node.Key);
                index++;
            }
        }

        EditorCallbackDispatcher.Invoke(progress, nodes.Count, nodes.Count,
            "完成", "PackageSourceCompiler.progress");
        return new PackageCompilationResult(paths);
    }

    private static IEnumerable<CompilationNode> CreateNodes(IEnumerable<BPackageDefinition> definitions)
    {
        foreach (var definition in definitions
                     .GroupBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (definition.Document.Runtime is { } runtime)
                yield return CreateNode(definition, "runtime", runtime);
            if (definition.Document.Editor is { } editor)
                yield return CreateNode(definition, "editor", editor);
        }
    }

    private static CompilationNode CreateNode(
        BPackageDefinition definition,
        string kind,
        PackageAssemblyDocument assembly)
    {
        var packageRoot = EditorHostServices.Paths.GetDirectoryName(EditorHostServices.Paths.GetFullPath(definition.Path))!;
        var sourceRoot = EditorHostServices.Paths.Combine(packageRoot, assembly.Assembly);
        if (!EditorHostServices.FileSystem.DirectoryExists(sourceRoot))
            throw new DirectoryNotFoundException(
                $"Package '{definition.Document.Id}' {kind} source directory was not found: '{sourceRoot}'.");

        var expectedPath = EditorHostServices.Paths.Combine(sourceRoot, $"{assembly.Assembly}.asmdef.yaml");
        var definitions = EditorHostServices.FileSystem.EnumerateFiles(
                sourceRoot, "*.asmdef.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (definitions.Length != 1 || !PathsEqual(definitions[0], expectedPath))
            throw new InvalidDataException(
                $"Package '{definition.Document.Id}' {kind} assembly '{assembly.Assembly}' must contain exactly " +
                $"one assembly definition named '{EditorHostServices.Paths.GetFileName(expectedPath)}' in '{sourceRoot}'.");

        var assemblyDefinition = YamlUtility.Load<AssemblyDefinitionDocument>(expectedPath);
        if (!assemblyDefinition.Name.Equals(assembly.Assembly, StringComparison.Ordinal) ||
            !assemblyDefinition.RootNamespace.Equals(assembly.RootNamespace, StringComparison.Ordinal) ||
            assemblyDefinition.EditorOnly != kind.Equals("editor", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Package '{definition.Document.Id}' {kind} assembly definition '{expectedPath}' must match " +
                $"package.yaml name, root namespace, and editor-only state.");

        return new CompilationNode(definition, kind, assembly, assemblyDefinition, expectedPath, sourceRoot);
    }

    private static void ValidateAssemblyDefinitions(IEnumerable<CompilationNode> sourceNodes)
    {
        var nodes = sourceNodes.ToArray();
        var nodesByAssembly = nodes
            .GroupBy(node => node.AssemblyDefinition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var duplicate = nodesByAssembly.FirstOrDefault(group => group.Value.Length != 1);
        if (duplicate.Value is { Length: > 1 })
            throw new InvalidDataException(
                $"Package assembly definition name '{duplicate.Key}' is declared more than once: " +
                string.Join(", ", duplicate.Value.Select(node => node.AssemblyDefinitionPath)));

        var nodesByKey = nodes.ToDictionary(node => node.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var expected = Dependencies(node, nodesByKey)
                .Select(dependency => dependency.AssemblyDefinition.Name)
                .Concat(node.Assembly.HostReferences)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual = node.AssemblyDefinition.References
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!expected.SetEquals(actual))
                throw new InvalidDataException(
                    $"Package assembly definition '{node.AssemblyDefinitionPath}' references do not match " +
                    $"package.yaml. Expected [{string.Join(", ", expected.Order())}], found " +
                    $"[{string.Join(", ", actual.Order())}].");
        }
    }

    private static IEnumerable<CompilationNode> Dependencies(
        CompilationNode node,
        IReadOnlyDictionary<string, CompilationNode> nodes)
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (node.Kind == "editor" && node.Definition.Document.Runtime is not null)
        {
            var ownRuntimeKey = Key(node.Definition.Document.Id, "runtime");
            if (nodes.TryGetValue(ownRuntimeKey, out var ownRuntime) && emitted.Add(ownRuntime.Key))
                yield return ownRuntime;
        }

        foreach (var dependency in node.Assembly.Dependencies)
        {
            var key = Key(dependency.PackageId, dependency.Target);
            if (nodes.TryGetValue(key, out var target))
            {
                if (emitted.Add(target.Key)) yield return target;
                continue;
            }
            if (!dependency.Optional)
                throw new InvalidDataException(
                    $"Package '{node.Definition.Document.Id}' {node.Kind} source depends on missing " +
                    $"{dependency.Target} assembly of '{dependency.PackageId}'.");
        }
    }

    private static Dictionary<string, string> CreateReferences(
        CompilationNode node,
        IReadOnlyDictionary<string, CompilationNode> nodes,
        IReadOnlyDictionary<string, string> compiledPaths)
    {
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEngine"] = typeof(BObject).Assembly.Location,
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] =
                typeof(IServiceCollection).Assembly.Location
        };
        if (node.Kind == "editor")
            references["BEngine.Editor"] = typeof(EditorWindow).Assembly.Location;

        var pending = new Stack<CompilationNode>(Dependencies(node, nodes));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out var dependency))
        {
            if (!visited.Add(dependency.Key)) continue;
            foreach (var nested in Dependencies(dependency, nodes)) pending.Push(nested);
            if (!compiledPaths.TryGetValue(dependency.Assembly.Assembly, out var path))
                throw new InvalidOperationException(
                    $"Package dependency '{dependency.Assembly.Assembly}' was not compiled before " +
                    $"'{node.Assembly.Assembly}'.");
            references[dependency.Assembly.Assembly] = path;
        }
        foreach (var name in node.Assembly.HostReferences)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 ||
                nodes.Values.Any(n => n.Assembly.Assembly.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Invalid host reference '{name}'; package assemblies require package dependencies.");
            var location = EditorHostServices.Paths.Combine(EditorHostServices.Paths.GetDirectoryName(typeof(EditorWindow).Assembly.Location)!, name + ".dll");
            if (!EditorHostServices.FileSystem.FileExists(location)) throw new FileNotFoundException($"Host assembly '{name}' is unavailable.", location);
            references[name] = location;
        }
        return references;
    }

    private static string CompileNode(
        ProjectWorkspace workspace,
        CompilationNode node,
        IReadOnlyDictionary<string, string> references,
        ProcessUtility processRunner,
        bool isolated,
        ScriptBuildConfiguration? playerConfiguration)
    {
        var sourceRoot = node.SourceRoot;
        var sources = EditorHostServices.FileSystem.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsBuildDirectory(sourceRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
            throw new InvalidDataException(
                $"Package '{node.Definition.Document.Id}' {node.Kind} assembly " +
                $"'{node.Assembly.Assembly}' contains no C# source files.");

        var configuration = playerConfiguration ?? ProjectScriptCompiler.CreateBuildConfiguration(
            workspace, node.Kind.Equals("editor", StringComparison.Ordinal));
        var buildId = ComputeBuildId(node, sourceRoot, sources, references, configuration);
        var instance = isolated ? null : EditorInstanceContext.current;
        var scriptAssembliesRoot = instance?.scriptAssembliesPath ?? workspace.ScriptAssembliesPath;
        var outputDirectory = EditorHostServices.Paths.Combine(
            ScriptAssemblyStore.GetAssemblyRoot(scriptAssembliesRoot, node.Assembly.Assembly),
            PhysicalBuildId(buildId));
        var assemblyPath = EditorHostServices.Paths.Combine(outputDirectory, $"{node.Assembly.Assembly}.dll");

        if (!EditorHostServices.FileSystem.FileExists(assemblyPath) && instance is not null)
        {
            var sharedDirectory = EditorHostServices.Paths.Combine(
                ScriptAssemblyStore.GetAssemblyRoot(workspace, node.Assembly.Assembly),
                PhysicalBuildId(buildId));
            var sharedAssembly = EditorHostServices.Paths.Combine(sharedDirectory, $"{node.Assembly.Assembly}.dll");
            if (EditorHostServices.FileSystem.FileExists(sharedAssembly)) CopyDirectory(sharedDirectory, outputDirectory);
        }

        if (!EditorHostServices.FileSystem.FileExists(assemblyPath))
            Build(processRunner, workspace, node, sources, references, configuration, outputDirectory, buildId, isolated);

        if (!configuration.IsPlayerBuild)
        {
            ScriptAssemblyStore.Publish(
                workspace, node.Assembly.Assembly, buildId, assemblyPath, scriptAssembliesRoot);
            if (!isolated) PublishShared(workspace, node.Assembly.Assembly, buildId, outputDirectory);
        }
        return assemblyPath;
    }

    private static void Build(
        ProcessUtility processRunner,
        ProjectWorkspace workspace,
        CompilationNode node,
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration,
        string outputDirectory,
        string buildId,
        bool isolated)
    {
        var instance = isolated ? null : EditorInstanceContext.current;
        var temporaryRoot = instance?.tempPath ?? workspace.TempPath;
        var logsRoot = instance?.logsPath ?? workspace.LogsPath;
        var buildDirectory = EditorHostServices.Paths.Combine(temporaryRoot, "PackageBuild", PhysicalBuildId(buildId));
        EditorHostServices.FileSystem.CreateDirectory(buildDirectory);
        EditorHostServices.FileSystem.CreateDirectory(outputDirectory);
        EditorHostServices.FileSystem.CreateDirectory(logsRoot);
        var projectPath = EditorHostServices.Paths.Combine(buildDirectory, $"{node.Assembly.Assembly}.csproj");
        var logPath = EditorHostServices.Paths.Combine(logsRoot,
            $"PackageCompilation.{Sanitize(node.Assembly.Assembly)}.log");
        var succeeded = false;
        try
        {
            var snapshotRoot = EditorHostServices.Paths.Combine(buildDirectory, "Sources");
            var instrumentedSources = SnapshotSources(
                workspace, node.SourceRoot, sources, snapshotRoot, configuration.DefineSymbols, !configuration.IsPlayerBuild);
            // MSBuild's reference resolver can reject long Windows HintPaths even though
            // the file APIs used by the editor can read the same assembly. Keep compiler
            // inputs close to the generated project, as the project-script compiler does.
            var compilerReferences = CopyCompilerReferences(references,
                EditorHostServices.Paths.Combine(buildDirectory, "References"));
            WriteProject(projectPath, node, instrumentedSources, compilerReferences, configuration,
                $"{snapshotRoot}={node.SourceRoot}");
            var request = new ProcessRunRequest("dotnet")
            {
                WorkingDirectory = workspace.RootPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            request.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            foreach (var argument in new[]
                     {
                         "build", projectPath, "--nologo", "--disable-build-servers", "-m:1", "-nr:false",
                         "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                         "-p:UseArtifactsOutput=false", "-p:ManagePackageVersionsCentrally=false",
                         "--configuration", configuration.Configuration, "--output", outputDirectory
                     })
                request.Arguments.Add(argument);

            var result = processRunner.Run(request);
            var diagnostics = result.StandardOutput + result.StandardError;
            EditorHostServices.FileSystem.WriteAllText(logPath, diagnostics);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Package '{node.Definition.Document.Id}' {node.Kind} assembly " +
                    $"'{node.Assembly.Assembly}' failed to compile. See '{logPath}'." +
                    EditorHostServices.Platform.NewLine + DiagnosticTail(diagnostics));

            CopyIntermediateAssembly(buildDirectory, node, configuration.TargetFramework, configuration.Configuration, outputDirectory);
            CopyExternalDependencies(buildDirectory, node, outputDirectory);
            var assemblyPath = EditorHostServices.Paths.Combine(outputDirectory, $"{node.Assembly.Assembly}.dll");
            if (!EditorHostServices.FileSystem.FileExists(assemblyPath))
                throw new FileNotFoundException(
                    $"Package compiler did not produce '{node.Assembly.Assembly}'.", assemblyPath);
            succeeded = true;
        }
        catch
        {
            TryDeleteDirectory(outputDirectory);
            throw;
        }
        finally
        {
            if (succeeded) TryDeleteDirectory(buildDirectory);
        }
    }

    private static void WriteProject(
        string path,
        CompilationNode node,
        IEnumerable<string> sources,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration,
        string pathMap)
    {
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("TargetFramework", configuration.TargetFramework),
            new XElement("LangVersion", "14.0"),
            new XElement("Nullable", "enable"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("AssemblyName", node.AssemblyDefinition.Name),
            new XElement("RootNamespace", node.AssemblyDefinition.RootNamespace),
            new XElement("AllowUnsafeBlocks", node.AssemblyDefinition.AllowUnsafeCode),
            new XElement("DefineConstants", string.Join(';', configuration.DefineSymbols)),
            new XElement("Deterministic", "true"),
            new XElement("Optimize", configuration.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase) ? "true" : "false"),
            new XElement("UseSharedCompilation", "false"),
            new XElement("CopyBuildOutputToOutputDirectory", "false"),
            new XElement("CopyOutputSymbolsToOutputDirectory", "false"),
            new XElement("NuGetAudit", "false"),
            new XElement("RestoreIgnoreFailedSources", "true"),
            new XElement("PathMap", pathMap));
        var sourceItems = new XElement("ItemGroup",
            sources.Select(source => new XElement("Compile", new XAttribute("Include", source))));
        var referenceItems = new XElement("ItemGroup",
            references.OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase)
                .Select(reference => new XElement("Reference",
                    new XAttribute("Include", reference.Key),
                    new XElement("HintPath", reference.Value),
                    new XElement("Private", "false"))));
        var packageItems = new XElement("ItemGroup",
            node.Assembly.PackageReferences
                .OrderBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase)
                .Select(reference => new XElement("PackageReference",
                    new XAttribute("Include", reference.Id),
                    new XAttribute("Version", reference.Version))));
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            propertyGroup, sourceItems, referenceItems, packageItems)).Save(path);
    }

    private static IReadOnlyDictionary<string, string> CopyCompilerReferences(
        IReadOnlyDictionary<string, string> references, string referenceRoot)
    {
        EditorHostServices.FileSystem.CreateDirectory(referenceRoot);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references.OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase))
        {
            var source = EditorHostServices.Paths.GetFullPath(reference.Value);
            if (!EditorHostServices.FileSystem.FileExists(source))
                throw new FileNotFoundException($"Compiler reference '{reference.Key}' was not found.", source);
            if (!copies.TryGetValue(source, out var destination))
            {
                destination = EditorHostServices.Paths.Combine(referenceRoot, $"r{copies.Count:x4}.dll");
                EditorHostServices.FileSystem.CopyFile(source, destination, true);
                copies.Add(source, destination);
            }
            result.Add(reference.Key, destination);
        }
        return result;
    }

    private static string ComputeBuildId(
        CompilationNode node,
        string sourceRoot,
        IEnumerable<string> sources,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "BEngine.PackageSource.v12");
        Append(hash, EditorLoopGuardInstrumenter.Revision);
        Append(hash, node.Definition.Document.Id);
        Append(hash, node.Definition.Document.PackageVersion);
        Append(hash, node.Kind);
        Append(hash, node.Assembly.Assembly);
        Append(hash, node.Assembly.RootNamespace);
        Append(hash, configuration.TargetFramework);
        Append(hash, configuration.Configuration);
        Append(hash, configuration.IsPlayerBuild.ToString());
        Append(hash, configuration.Platform.ToString());
        foreach (var symbol in configuration.DefineSymbols) Append(hash, symbol);
        hash.AppendData(EditorHostServices.FileSystem.ReadAllBytes(node.AssemblyDefinitionPath));
        foreach (var source in sources)
        {
            Append(hash, EditorHostServices.Paths.GetRelativePath(sourceRoot, source).Replace(EditorHostServices.Paths.DirectorySeparatorChar, '/'));
            hash.AppendData(EditorHostServices.FileSystem.ReadAllBytes(source));
        }
        foreach (var reference in references.OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Key);
            hash.AppendData(EditorHostServices.FileSystem.ReadAllBytes(reference.Value));
        }
        foreach (var reference in node.Assembly.PackageReferences
                     .OrderBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Id);
            Append(hash, reference.Version);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static IReadOnlyList<string> SnapshotSources(
        ProjectWorkspace workspace,
        string sourceRoot,
        IEnumerable<string> sources,
        string snapshotRoot,
        IReadOnlyList<string> preprocessorSymbols,
        bool instrument)
    {
        var snapshots = new List<string>();
        foreach (var source in sources)
        {
            var relative = EditorHostServices.Paths.GetRelativePath(sourceRoot, source);
            if (relative.StartsWith("..", StringComparison.Ordinal))
                relative = EditorHostServices.Paths.Combine("External", $"{snapshots.Count:x4}-{EditorHostServices.Paths.GetFileName(source)}");
            var destination = EditorHostServices.Paths.Combine(snapshotRoot, relative);
            EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(destination)!);
            var displayPath = EditorHostServices.Paths.GetRelativePath(workspace.RootPath, source)
                .Replace(EditorHostServices.Paths.DirectorySeparatorChar, '/');
            var instrumented = EditorLoopGuardInstrumenter.Instrument(
                EditorHostServices.FileSystem.ReadAllBytes(source), displayPath, preprocessorSymbols, instrument);
            EditorHostServices.FileSystem.WriteAllText(destination, instrumented);
            snapshots.Add(destination);
        }
        return snapshots;
    }

    private static void PublishShared(
        ProjectWorkspace workspace,
        string assemblyName,
        string buildId,
        string sourceDirectory)
    {
        if (EditorInstanceContext.current is null) return;
        var destination = EditorHostServices.Paths.Combine(ScriptAssemblyStore.GetAssemblyRoot(workspace, assemblyName),
            PhysicalBuildId(buildId));
        if (!EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(destination, $"{assemblyName}.dll")))
            CopyDirectory(sourceDirectory, destination);
        var assemblyPath = EditorHostServices.Paths.Combine(destination, $"{assemblyName}.dll");
        ScriptAssemblyStore.Publish(workspace, assemblyName, buildId, assemblyPath);
    }

    private static void CopyDirectory(string source, string destination)
    {
        EditorHostServices.FileSystem.CreateDirectory(destination);
        foreach (var directory in EditorHostServices.FileSystem.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.Combine(destination, EditorHostServices.Paths.GetRelativePath(source, directory)));
        foreach (var file in EditorHostServices.FileSystem.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = EditorHostServices.Paths.Combine(destination, EditorHostServices.Paths.GetRelativePath(source, file));
            EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(target)!);
            EditorHostServices.FileSystem.CopyFile(file, target, true);
        }
    }

    private static bool ContainsBuildDirectory(string root, string path) =>
        EditorHostServices.Paths.GetRelativePath(root, path).Split(EditorHostServices.Paths.DirectorySeparatorChar, EditorHostServices.Paths.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static string DiagnosticTail(string diagnostics) => string.Join(EditorHostServices.Platform.NewLine,
        diagnostics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(20));

    private static void CopyIntermediateAssembly(
        string buildDirectory,
        CompilationNode node,
        string targetFramework,
        string configuration,
        string outputDirectory)
    {
        var intermediateRoot = EditorHostServices.Paths.Combine(buildDirectory, "obj", configuration, targetFramework);
        var intermediateAssembly = EditorHostServices.Paths.Combine(intermediateRoot, $"{node.Assembly.Assembly}.dll");
        if (!EditorHostServices.FileSystem.FileExists(intermediateAssembly))
            throw new FileNotFoundException(
                $"Package compiler did not produce intermediate assembly '{node.Assembly.Assembly}'.",
                intermediateAssembly);

        EditorHostServices.FileSystem.CreateDirectory(outputDirectory);
        EditorHostServices.FileSystem.CopyFile(intermediateAssembly,
            EditorHostServices.Paths.Combine(outputDirectory, EditorHostServices.Paths.GetFileName(intermediateAssembly)), true);
        var symbols = EditorHostServices.ChangeExtension(intermediateAssembly, ".pdb");
        if (EditorHostServices.FileSystem.FileExists(symbols))
            EditorHostServices.FileSystem.CopyFile(symbols, EditorHostServices.Paths.Combine(outputDirectory, EditorHostServices.Paths.GetFileName(symbols)), true);
    }

    private static void CopyExternalDependencies(
        string buildDirectory,
        CompilationNode node,
        string outputDirectory)
    {
        if (node.Assembly.PackageReferences.Count == 0) return;
        var assetsPath = EditorHostServices.Paths.Combine(buildDirectory, "obj", "project.assets.json");
        if (!EditorHostServices.FileSystem.FileExists(assetsPath))
            throw new FileNotFoundException(
                $"Package compiler did not produce NuGet assets for '{node.Assembly.Assembly}'.", assetsPath);

        using var assets = JsonDocument.Parse(EditorHostServices.FileSystem.ReadAllText(assetsPath));
        var root = assets.RootElement;
        var packageFolders = root.GetProperty("packageFolders").EnumerateObject()
            .Select(folder => EditorHostServices.Paths.GetFullPath(folder.Name)).ToArray();
        var libraries = root.GetProperty("libraries");
        var target = root.GetProperty("targets").EnumerateObject()
            .OrderBy(candidate => candidate.Name.Contains('/', StringComparison.Ordinal) ? 1 : 0)
            .First().Value;
        var copied = 0;
        foreach (var library in target.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package" ||
                !libraries.TryGetProperty(library.Name, out var libraryDocument) ||
                !libraryDocument.TryGetProperty("path", out var packagePathElement)) continue;
            var packagePath = packagePathElement.GetString();
            if (string.IsNullOrWhiteSpace(packagePath)) continue;

            if (library.Value.TryGetProperty("runtime", out var runtime))
                foreach (var asset in runtime.EnumerateObject())
                    copied += CopyNuGetAsset(packageFolders, packagePath, asset.Name,
                        EditorHostServices.Paths.GetFileName(asset.Name), outputDirectory);
            if (!library.Value.TryGetProperty("runtimeTargets", out var runtimeTargets)) continue;
            foreach (var asset in runtimeTargets.EnumerateObject())
                copied += CopyNuGetAsset(packageFolders, packagePath, asset.Name,
                    asset.Name.Replace('/', EditorHostServices.Paths.DirectorySeparatorChar), outputDirectory);
        }

        if (copied == 0)
            throw new InvalidDataException(
                $"Package '{node.Definition.Document.Id}' declares external references, but no runtime assets " +
                $"were resolved for '{node.Assembly.Assembly}'.");
    }

    private static int CopyNuGetAsset(
        IEnumerable<string> packageFolders,
        string packagePath,
        string assetPath,
        string destinationPath,
        string outputDirectory)
    {
        if (assetPath == "_._") return 0;
        var source = packageFolders.Select(folder => EditorHostServices.Paths.Combine(folder,
                packagePath.Replace('/', EditorHostServices.Paths.DirectorySeparatorChar),
                assetPath.Replace('/', EditorHostServices.Paths.DirectorySeparatorChar)))
            .FirstOrDefault(EditorHostServices.FileSystem.FileExists);
        if (source is null) return 0;
        var destination = EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.Combine(outputDirectory, destinationPath));
        var outputRoot = EditorHostServices.Paths.GetFullPath(outputDirectory).TrimEnd(EditorHostServices.Paths.DirectorySeparatorChar) +
                         EditorHostServices.Paths.DirectorySeparatorChar;
        if (!destination.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"NuGet asset path escapes package output: '{assetPath}'.");
        EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(destination)!);
        EditorHostServices.FileSystem.CopyFile(source, destination, true);
        var symbols = EditorHostServices.ChangeExtension(source, ".pdb");
        if (EditorHostServices.FileSystem.FileExists(symbols)) EditorHostServices.FileSystem.CopyFile(symbols, EditorHostServices.ChangeExtension(destination, ".pdb"), true);
        return 1;
    }

    private static string Key(string packageId, string kind) => $"{packageId}\0{kind}";

    private static string PhysicalBuildId(string buildId) => buildId[..24];

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        EditorHostServices.Paths.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static bool PathsEqual(string left, string right) => string.Equals(
        EditorHostServices.Paths.GetFullPath(left), EditorHostServices.Paths.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (EditorHostServices.FileSystem.DirectoryExists(path)) EditorHostServices.FileSystem.DeleteDirectory(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private readonly record struct CompilationNode(
        BPackageDefinition Definition,
        string Kind,
        PackageAssemblyDocument Assembly,
        AssemblyDefinitionDocument AssemblyDefinition,
        string AssemblyDefinitionPath,
        string SourceRoot)
    {
        public string Key => PackageSourceCompiler.Key(Definition.Document.Id, Kind);
    }
}
