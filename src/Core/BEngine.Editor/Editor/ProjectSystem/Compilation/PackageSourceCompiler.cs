using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Serialization;

namespace BEngine.ProjectSystem;

internal static class PackageSourceCompiler
{
    internal static PackageCompilationResult CompileEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions,
        Action<int, int, string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(definitions);

        var nodes = CreateNodes(definitions).ToDictionary(node => node.Key, StringComparer.OrdinalIgnoreCase);
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
                EditorCallbackDispatcher.Invoke(progress, index, nodes.Count,
                    $"{node.Definition.Document.DisplayName} ({node.Kind})",
                    "PackageSourceCompiler.progress");
                var references = CreateReferences(node, nodes, paths);
                var assemblyPath = CompileNode(workspace, node, references);
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
        var packageRoot = Path.GetDirectoryName(Path.GetFullPath(definition.Path))!;
        var sourceRoot = Path.Combine(packageRoot, assembly.Assembly);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException(
                $"Package '{definition.Document.Id}' {kind} source directory was not found: '{sourceRoot}'.");

        var expectedPath = Path.Combine(sourceRoot, $"{assembly.Assembly}.asmdef.yaml");
        var definitions = Directory.EnumerateFiles(
                sourceRoot, "*.asmdef.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (definitions.Length != 1 || !PathsEqual(definitions[0], expectedPath))
            throw new InvalidDataException(
                $"Package '{definition.Document.Id}' {kind} assembly '{assembly.Assembly}' must contain exactly " +
                $"one assembly definition named '{Path.GetFileName(expectedPath)}' in '{sourceRoot}'.");

        var assemblyDefinition = Document.Load<AssemblyDefinitionDocument>(expectedPath);
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

        foreach (var dependency in Dependencies(node, nodes))
        {
            if (!compiledPaths.TryGetValue(dependency.Assembly.Assembly, out var path))
                throw new InvalidOperationException(
                    $"Package dependency '{dependency.Assembly.Assembly}' was not compiled before " +
                    $"'{node.Assembly.Assembly}'.");
            references[dependency.Assembly.Assembly] = path;
        }
        return references;
    }

    private static string CompileNode(
        ProjectWorkspace workspace,
        CompilationNode node,
        IReadOnlyDictionary<string, string> references)
    {
        var sourceRoot = node.SourceRoot;
        var sources = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsBuildDirectory(sourceRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
            throw new InvalidDataException(
                $"Package '{node.Definition.Document.Id}' {node.Kind} assembly " +
                $"'{node.Assembly.Assembly}' contains no C# source files.");

        var buildId = ComputeBuildId(node, sourceRoot, sources, references);
        var instance = EditorInstanceContext.current;
        var scriptAssembliesRoot = instance?.scriptAssembliesPath ?? workspace.ScriptAssembliesPath;
        var outputDirectory = Path.Combine(
            ScriptAssemblyStore.GetAssemblyRoot(scriptAssembliesRoot, node.Assembly.Assembly),
            PhysicalBuildId(buildId));
        var assemblyPath = Path.Combine(outputDirectory, $"{node.Assembly.Assembly}.dll");

        if (!File.Exists(assemblyPath) && instance is not null)
        {
            var sharedDirectory = Path.Combine(
                ScriptAssemblyStore.GetAssemblyRoot(workspace, node.Assembly.Assembly),
                PhysicalBuildId(buildId));
            var sharedAssembly = Path.Combine(sharedDirectory, $"{node.Assembly.Assembly}.dll");
            if (File.Exists(sharedAssembly)) CopyDirectory(sharedDirectory, outputDirectory);
        }

        if (!File.Exists(assemblyPath))
            Build(workspace, node, sources, references, outputDirectory, buildId);

        ScriptAssemblyStore.Publish(
            workspace, node.Assembly.Assembly, buildId, assemblyPath, scriptAssembliesRoot);
        PublishShared(workspace, node.Assembly.Assembly, buildId, outputDirectory);
        return assemblyPath;
    }

    private static void Build(
        ProjectWorkspace workspace,
        CompilationNode node,
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string> references,
        string outputDirectory,
        string buildId)
    {
        var instance = EditorInstanceContext.current;
        var temporaryRoot = instance?.tempPath ?? workspace.TempPath;
        var logsRoot = instance?.logsPath ?? workspace.LogsPath;
        var buildDirectory = Path.Combine(temporaryRoot, "PackageBuild", PhysicalBuildId(buildId));
        Directory.CreateDirectory(buildDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(logsRoot);
        var projectPath = Path.Combine(buildDirectory, $"{node.Assembly.Assembly}.csproj");
        var logPath = Path.Combine(logsRoot,
            $"PackageCompilation.{Sanitize(node.Assembly.Assembly)}.log");
        var succeeded = false;
        try
        {
            WriteProject(projectPath, node, sources, references);
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workspace.RootPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            foreach (var argument in new[]
                     {
                         "build", projectPath, "--nologo", "--disable-build-servers", "-m:1", "-nr:false",
                         "--configuration", "Debug", "--output", outputDirectory
                     })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException("Could not start dotnet package compiler.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            var diagnostics = stdout.Result + stderr.Result;
            File.WriteAllText(logPath, diagnostics);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Package '{node.Definition.Document.Id}' {node.Kind} assembly " +
                    $"'{node.Assembly.Assembly}' failed to compile. See '{logPath}'." +
                    Environment.NewLine + DiagnosticTail(diagnostics));

            CopyIntermediateAssembly(buildDirectory, node, outputDirectory);
            CopyExternalDependencies(buildDirectory, node, outputDirectory);
            var assemblyPath = Path.Combine(outputDirectory, $"{node.Assembly.Assembly}.dll");
            if (!File.Exists(assemblyPath))
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
        IReadOnlyDictionary<string, string> references)
    {
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("TargetFramework", node.Kind == "editor" ? "net10.0-windows" : "net10.0"),
            new XElement("LangVersion", "14.0"),
            new XElement("Nullable", "enable"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("AssemblyName", node.AssemblyDefinition.Name),
            new XElement("RootNamespace", node.AssemblyDefinition.RootNamespace),
            new XElement("AllowUnsafeBlocks", node.AssemblyDefinition.AllowUnsafeCode),
            new XElement("Deterministic", "true"),
            new XElement("UseSharedCompilation", "false"),
            new XElement("CopyBuildOutputToOutputDirectory", "false"),
            new XElement("CopyOutputSymbolsToOutputDirectory", "false"),
            new XElement("NuGetAudit", "false"),
            new XElement("RestoreIgnoreFailedSources", "true"));
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

    private static string ComputeBuildId(
        CompilationNode node,
        string sourceRoot,
        IEnumerable<string> sources,
        IReadOnlyDictionary<string, string> references)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "BEngine.PackageSource.v8");
        Append(hash, node.Definition.Document.Id);
        Append(hash, node.Definition.Document.PackageVersion);
        Append(hash, node.Kind);
        Append(hash, node.Assembly.Assembly);
        Append(hash, node.Assembly.RootNamespace);
        hash.AppendData(File.ReadAllBytes(node.AssemblyDefinitionPath));
        foreach (var source in sources)
        {
            Append(hash, Path.GetRelativePath(sourceRoot, source).Replace(Path.DirectorySeparatorChar, '/'));
            hash.AppendData(File.ReadAllBytes(source));
        }
        foreach (var reference in references.OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Key);
            hash.AppendData(File.ReadAllBytes(reference.Value));
        }
        foreach (var reference in node.Assembly.PackageReferences
                     .OrderBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Id);
            Append(hash, reference.Version);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void PublishShared(
        ProjectWorkspace workspace,
        string assemblyName,
        string buildId,
        string sourceDirectory)
    {
        if (EditorInstanceContext.current is null) return;
        var destination = Path.Combine(ScriptAssemblyStore.GetAssemblyRoot(workspace, assemblyName),
            PhysicalBuildId(buildId));
        if (!File.Exists(Path.Combine(destination, $"{assemblyName}.dll")))
            CopyDirectory(sourceDirectory, destination);
        var assemblyPath = Path.Combine(destination, $"{assemblyName}.dll");
        ScriptAssemblyStore.Publish(workspace, assemblyName, buildId, assemblyPath);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static bool ContainsBuildDirectory(string root, string path) =>
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static string DiagnosticTail(string diagnostics) => string.Join(Environment.NewLine,
        diagnostics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(20));

    private static void CopyIntermediateAssembly(
        string buildDirectory,
        CompilationNode node,
        string outputDirectory)
    {
        var targetFramework = node.Kind == "editor" ? "net10.0-windows" : "net10.0";
        var intermediateRoot = Path.Combine(buildDirectory, "obj", "Debug", targetFramework);
        var intermediateAssembly = Path.Combine(intermediateRoot, $"{node.Assembly.Assembly}.dll");
        var deadline = Environment.TickCount64 + 5_000;
        while (!File.Exists(intermediateAssembly) && Environment.TickCount64 < deadline)
            Thread.Sleep(20);
        if (!File.Exists(intermediateAssembly))
            throw new FileNotFoundException(
                $"Package compiler did not produce intermediate assembly '{node.Assembly.Assembly}'.",
                intermediateAssembly);

        Directory.CreateDirectory(outputDirectory);
        File.Copy(intermediateAssembly,
            Path.Combine(outputDirectory, Path.GetFileName(intermediateAssembly)), true);
        var symbols = Path.ChangeExtension(intermediateAssembly, ".pdb");
        if (File.Exists(symbols))
            File.Copy(symbols, Path.Combine(outputDirectory, Path.GetFileName(symbols)), true);
    }

    private static void CopyExternalDependencies(
        string buildDirectory,
        CompilationNode node,
        string outputDirectory)
    {
        if (node.Assembly.PackageReferences.Count == 0) return;
        var assetsPath = Path.Combine(buildDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            throw new FileNotFoundException(
                $"Package compiler did not produce NuGet assets for '{node.Assembly.Assembly}'.", assetsPath);

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = assets.RootElement;
        var packageFolders = root.GetProperty("packageFolders").EnumerateObject()
            .Select(folder => Path.GetFullPath(folder.Name)).ToArray();
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
                        Path.GetFileName(asset.Name), outputDirectory);
            if (!library.Value.TryGetProperty("runtimeTargets", out var runtimeTargets)) continue;
            foreach (var asset in runtimeTargets.EnumerateObject())
                copied += CopyNuGetAsset(packageFolders, packagePath, asset.Name,
                    asset.Name.Replace('/', Path.DirectorySeparatorChar), outputDirectory);
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
        var source = packageFolders.Select(folder => Path.Combine(folder,
                packagePath.Replace('/', Path.DirectorySeparatorChar),
                assetPath.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(File.Exists);
        if (source is null) return 0;
        var destination = Path.GetFullPath(Path.Combine(outputDirectory, destinationPath));
        var outputRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
        if (!destination.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"NuGet asset path escapes package output: '{assetPath}'.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
        var symbols = Path.ChangeExtension(source, ".pdb");
        if (File.Exists(symbols)) File.Copy(symbols, Path.ChangeExtension(destination, ".pdb"), true);
        return 1;
    }

    private static string Key(string packageId, string kind) => $"{packageId}\0{kind}";

    private static string PhysicalBuildId(string buildId) => buildId[..24];

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
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
