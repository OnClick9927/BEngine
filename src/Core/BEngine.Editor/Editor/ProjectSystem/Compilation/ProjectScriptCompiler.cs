using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ProjectSystem;

public static class ProjectScriptCompiler
{
    private static readonly Lock LoadContextGate = new();
    private static readonly List<ProjectScriptLoadContext> ActiveLoadContexts = [];

    internal static ScriptBuildConfiguration RuntimeBuildConfiguration { get; } =
        CreateBuildConfigurationDefaults(editor: false);

    public static Assembly? CompileAndLoad(ProjectWorkspace workspace)
    {
        var packages = RuntimePackageLoader.LoadEnabled(workspace);
        return CompileAndLoad(workspace, packages);
    }

    public static Assembly? CompileAndLoad(ProjectWorkspace workspace, RuntimePackageSet packages)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packages);
        ReleaseLoadContexts();
        var configuration = CreateBuildConfiguration(workspace, editor: false);
        var externalAssemblies = packages.AssemblyReferences.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = new ProjectAssemblyDatabase(workspace).BuildGraph(
            configuration.Platform, configuration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            externalAssemblies, includeEditorAssemblies: false);
        var result = CompileAndLoadGraph(
            workspace,
            graph.RuntimeAssemblies,
            packages.AssemblyReferences,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            configuration,
            publishRuntimeManifest: true);
        return result.EntryAssembly;
    }

    internal static void ReleaseLoadContexts()
    {
        ProjectScriptLoadContext[] contexts;
        lock (LoadContextGate) contexts = ActiveLoadContexts.ToArray();
        ReleaseLoadContexts(contexts, collect: true);
    }

    internal static ProjectAssemblyCompilationResult CompileAndLoadGraph(
        ProjectWorkspace workspace,
        IReadOnlyList<ProjectAssemblyNode> nodes,
        IReadOnlyDictionary<string, string> baseReferences,
        IReadOnlyDictionary<string, string> existingProjectReferences,
        ScriptBuildConfiguration configuration,
        bool publishRuntimeManifest)
    {
        var build = CompileGraph(workspace, nodes, baseReferences, existingProjectReferences,
            configuration, publishRuntimeManifest, raiseCompilationEvents: true);
        return LoadCompiledGraph(build);
    }

    internal static ProjectAssemblyBuildResult CompileGraph(
        ProjectWorkspace workspace,
        IReadOnlyList<ProjectAssemblyNode> nodes,
        IReadOnlyDictionary<string, string> baseReferences,
        IReadOnlyDictionary<string, string> existingProjectReferences,
        ScriptBuildConfiguration configuration,
        bool publishRuntimeManifest,
        bool raiseCompilationEvents,
        bool publishOutputs = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var availableProjectReferences = existingProjectReferences.ToDictionary(
            reference => reference.Key, reference => reference.Value, StringComparer.OrdinalIgnoreCase);
        var compiled = new List<CompiledProjectAssembly>(nodes.Count);
        var referenceRoot = Path.Combine(EditorInstanceContext.current?.tempPath ?? workspace.TempPath,
            "CompilerReferences", Guid.NewGuid().ToString("N")[..12]);
        var referenceCopies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var references = baseReferences.ToDictionary(
                    reference => reference.Key, reference => reference.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var reference in node.References)
                {
                    if (availableProjectReferences.TryGetValue(reference, out var projectPath))
                        references[reference] = projectPath;
                    else if (!references.ContainsKey(reference))
                        throw new InvalidDataException(
                            $"Missing assembly reference '{reference}' required by '{node.Name}'.");
                }

                var compilerReferences = CopyCompilerReferences(references, referenceRoot, referenceCopies);
                var artifact = CompileAssembly(workspace, node, compilerReferences, configuration,
                    raiseCompilationEvents, cancellationToken);
                compiled.Add(artifact);
                availableProjectReferences[node.Name] = artifact.AssemblyPath;
            }
        }
        finally { TryDeleteDirectory(referenceRoot); }

        if (publishOutputs) PublishCompiledAssemblies(workspace, compiled, publishRuntimeManifest);
        return new ProjectAssemblyBuildResult(compiled,
            new Dictionary<string, string>(baseReferences, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(existingProjectReferences, StringComparer.OrdinalIgnoreCase));
    }

    internal static ProjectAssemblyCompilationResult LoadCompiledGraph(ProjectAssemblyBuildResult build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var loaded = LoadAssemblies(build.CompiledAssemblies, build.BaseReferences,
            build.ExistingProjectReferences);
        return new ProjectAssemblyCompilationResult(build.CompiledAssemblies, loaded);
    }

    internal static void ReplaceLoadedGraphs(
        ProjectWorkspace workspace,
        ProjectAssemblyBuildResult runtime,
        ProjectAssemblyBuildResult editor)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(editor);
        ProjectScriptLoadContext[] previous;
        lock (LoadContextGate) previous = ActiveLoadContexts.ToArray();
        try
        {
            _ = LoadCompiledGraph(runtime);
            _ = LoadCompiledGraph(editor);
            PublishCompiledAssemblies(workspace, runtime.CompiledAssemblies, publishRuntimeManifest: true);
            PublishCompiledAssemblies(workspace, editor.CompiledAssemblies, publishRuntimeManifest: false);
            ReleaseLoadContexts(previous, collect: true);
        }
        catch
        {
            ProjectScriptLoadContext[] staged;
            lock (LoadContextGate)
                staged = ActiveLoadContexts.Where(context => !previous.Contains(context)).ToArray();
            ReleaseLoadContexts(staged, collect: false);
            RuntimeTypeCache.RegisterAssemblies(previous.SelectMany(context => context.Assemblies));
            throw;
        }
    }

    internal static Assembly? CompileAndLoadAssembly(
        ProjectWorkspace workspace,
        string sourceDirectory,
        string assemblyName,
        string logFileName,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration)
    {
        _ = logFileName;
        var sources = Directory.Exists(sourceDirectory)
            ? Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (sources.Length == 0) return null;
        var node = new ProjectAssemblyNode
        {
            Name = assemblyName,
            RootNamespace = assemblyName,
            DefinitionPath = string.Empty,
            SourcePaths = sources,
            References = [],
            EditorOnly = configuration.IsEditor,
            AutoReferenced = true,
            AllowUnsafeCode = false
        };
        return CompileAndLoadGraph(workspace, [node], references,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), configuration,
            publishRuntimeManifest: !configuration.IsEditor).EntryAssembly;
    }

    internal static ScriptBuildConfiguration CreateBuildConfiguration(
        ProjectWorkspace workspace,
        bool editor)
    {
        var symbols = BEngineCompilationSymbols.Create(
            editor, BEngineCompilationSymbols.DebugConfiguration).ToHashSet(StringComparer.Ordinal);
        var settingsPath = workspace.ProjectSettingsFilePath;
        if (File.Exists(settingsPath))
        {
            var settings = YamlUtility.Load<ProjectSettingsData>(settingsPath);
            foreach (var symbol in settings.ScriptingDefineSymbols ?? [])
            {
                var normalized = symbol?.Trim();
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                ValidateDefineSymbol(normalized, settingsPath);
                symbols.Add(normalized);
            }
        }
        return new ScriptBuildConfiguration(
            editor ? "net10.0-windows" : "net10.0",
            BEngineCompilationSymbols.ResolvePlatform(editor),
            symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
            editor);
    }

    internal static void WriteBuildProject(
        string path,
        ProjectAssemblyNode node,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration,
        string? pathMap = null)
    {
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("TargetFramework", configuration.TargetFramework),
            new XElement("LangVersion", "14.0"),
            new XElement("Nullable", "enable"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("AssemblyName", node.Name),
            new XElement("RootNamespace", node.RootNamespace),
            new XElement("AllowUnsafeBlocks", node.AllowUnsafeCode),
            new XElement("DefineConstants", string.Join(';', configuration.DefineSymbols)),
            new XElement("Deterministic", "true"),
            new XElement("UseSharedCompilation", "false"),
            new XElement("CopyBuildOutputToOutputDirectory", "false"),
            new XElement("CopyOutputSymbolsToOutputDirectory", "false"),
            new XElement("NuGetAudit", "false"),
            new XElement("RestoreIgnoreFailedSources", "true"));
        if (!string.IsNullOrWhiteSpace(pathMap)) propertyGroup.Add(new XElement("PathMap", pathMap));
        var sourceItems = new XElement("ItemGroup", node.SourcePaths.Select(source =>
            new XElement("Compile", new XAttribute("Include", source))));
        var referenceItems = new XElement("ItemGroup", references
            .OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase)
            .Select(reference => new XElement("Reference",
                new XAttribute("Include", reference.Key),
                new XElement("HintPath", reference.Value),
                new XElement("Private", "false"))));
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            propertyGroup, sourceItems, referenceItems)).Save(path);
    }

    internal static void AddPackageAssemblyReference(
        ProjectWorkspace workspace,
        string packageId,
        string kind,
        BEngine.Editor.Documents.PackageAssemblyDocument? assembly,
        IDictionary<string, string> references,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths)
    {
        if (assembly is null) return;
        references[assembly.Assembly] = ResolveAssemblyPath(
            workspace, packageId, kind, assembly.Assembly, preferredAssemblyPaths);
    }

    internal static string ResolveAssemblyPath(
        ProjectWorkspace workspace,
        string packageId,
        string kind,
        string assemblyName,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
            string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (ReadableLocation(loaded) is { } loadedPath) return loadedPath;

        if (preferredAssemblyPaths is not null &&
            preferredAssemblyPaths.TryGetValue(assemblyName, out var preferredPath) &&
            File.Exists(preferredPath))
            return Path.GetFullPath(preferredPath);

        var applicationPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        if (File.Exists(applicationPath)) return Path.GetFullPath(applicationPath);

        if (RuntimePackageLoader.ResolveAssemblyPath(workspace, assemblyName) is { } packagePath)
            return packagePath;

        Exception? loadFailure = null;
        try
        {
            var resolved = Assembly.Load(new AssemblyName(assemblyName));
            if (ReadableLocation(resolved) is { } resolvedPath) return resolvedPath;
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            loadFailure = exception;
        }

        throw new FileNotFoundException(
            $"Enabled package '{packageId}' declares {kind} assembly '{assemblyName}', but no readable DLL " +
            $"was found. Checked loaded assemblies, '{applicationPath}', and Assembly.Load('{assemblyName}').",
            loadFailure);
    }

    internal static IReadOnlyDictionary<string, string> PreferredAssemblyPaths(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The preferred assembly was not found.", fullPath);
        var name = AssemblyName.GetAssemblyName(fullPath).Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BadImageFormatException($"The preferred assembly '{fullPath}' has no assembly name.");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = fullPath };
    }

    private static CompiledProjectAssembly CompileAssembly(
        ProjectWorkspace workspace,
        ProjectAssemblyNode node,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration,
        bool raiseCompilationEvents,
        CancellationToken cancellationToken)
    {
        var sourceSnapshots = node.SourcePaths.ToDictionary(
            source => source,
            File.ReadAllBytes,
            StringComparer.OrdinalIgnoreCase);
        var buildId = ComputeBuildId(workspace, node, references, configuration, sourceSnapshots);
        var instance = EditorInstanceContext.current;
        var temporaryRoot = instance?.tempPath ?? workspace.TempPath;
        var scriptAssembliesRoot = instance?.scriptAssembliesPath ?? workspace.ScriptAssembliesPath;
        var logsRoot = instance?.logsPath ?? workspace.LogsPath;
        var outputDirectory = Path.Combine(
            ScriptAssemblyStore.GetAssemblyRoot(scriptAssembliesRoot, node.Name), PhysicalBuildId(buildId));
        var assemblyPath = Path.Combine(outputDirectory, $"{node.Name}.dll");

        if (!File.Exists(assemblyPath) && instance is not null)
        {
            var sharedDirectory = Path.Combine(
                ScriptAssemblyStore.GetAssemblyRoot(workspace, node.Name), PhysicalBuildId(buildId));
            var sharedAssembly = Path.Combine(sharedDirectory, $"{node.Name}.dll");
            if (File.Exists(sharedAssembly)) CopyDirectory(sharedDirectory, outputDirectory);
        }

        if (!File.Exists(assemblyPath))
        {
            var buildDirectory = Path.Combine(temporaryRoot, "ScriptBuild", PhysicalBuildId(buildId));
            Directory.CreateDirectory(buildDirectory);
            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(logsRoot);
            var projectPath = Path.Combine(buildDirectory, "build.csproj");
            var logPath = Path.Combine(logsRoot, $"ScriptCompilation.{Sanitize(node.Name)}.log");
            var phaseLogPath = Path.Combine(logsRoot,
                configuration.IsEditor ? "EditorScriptCompilation.log" : "ScriptCompilation.log");
            if (raiseCompilationEvents) CompilationPipeline.RaiseAssemblyCompilationStarted(assemblyPath);
            try
            {
                var snapshotRoot = Path.Combine(buildDirectory, "Sources");
                var snapshotPaths = new List<string>(node.SourcePaths.Count);
                for (var index = 0; index < node.SourcePaths.Count; index++)
                {
                    var source = node.SourcePaths[index];
                    var relative = Path.GetRelativePath(workspace.RootPath, source);
                    if (relative.StartsWith("..", StringComparison.Ordinal))
                        relative = Path.Combine("External", $"{index:x4}-{Path.GetFileName(source)}");
                    var snapshotPath = Path.Combine(snapshotRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
                    File.WriteAllBytes(snapshotPath, sourceSnapshots[source]);
                    snapshotPaths.Add(snapshotPath);
                }
                var snapshotNode = new ProjectAssemblyNode
                {
                    Name = node.Name,
                    RootNamespace = node.RootNamespace,
                    DefinitionPath = node.DefinitionPath,
                    SourcePaths = snapshotPaths,
                    References = node.References,
                    EditorOnly = node.EditorOnly,
                    AutoReferenced = node.AutoReferenced,
                    AllowUnsafeCode = node.AllowUnsafeCode,
                    Definition = node.Definition
                };
                WriteBuildProject(projectPath, snapshotNode, references, configuration,
                    $"{snapshotRoot}={workspace.RootPath}");
                Build(workspace, projectPath, outputDirectory, logPath, phaseLogPath, node.Name,
                    configuration.TargetFramework, cancellationToken);
                if (raiseCompilationEvents) CompilationPipeline.RaiseAssemblyCompilationFinished(assemblyPath, []);
            }
            catch (Exception exception)
            {
                if (raiseCompilationEvents)
                    CompilationPipeline.RaiseAssemblyCompilationFinished(assemblyPath,
                        [new CompilerMessage(exception.Message, node.DefinitionPath, 0, 0,
                            CompilerMessageType.Error)]);
                TryDeleteDirectory(outputDirectory);
                throw;
            }
            finally
            {
                TryDeleteDirectory(buildDirectory);
            }
        }

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"The {node.Name} assembly was not produced.", assemblyPath);
        return new CompiledProjectAssembly
        {
            Node = node,
            BuildId = buildId,
            AssemblyPath = assemblyPath
        };
    }

    private static void Build(
        ProjectWorkspace workspace,
        string projectPath,
        string outputDirectory,
        string logPath,
        string phaseLogPath,
        string assemblyName,
        string targetFramework,
        CancellationToken cancellationToken)
    {
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
                            throw new InvalidOperationException("Could not start dotnet script compiler.");
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = stdout.Result + stderr.Result;
        File.WriteAllText(logPath, diagnostics);
        File.WriteAllText(phaseLogPath, diagnostics);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{assemblyName} failed to compile. See '{logPath}'." + Environment.NewLine +
                DiagnosticTail(diagnostics));
        CopyIntermediateAssembly(Path.GetDirectoryName(projectPath)!, outputDirectory, assemblyName,
            targetFramework);
    }

    private static void CopyIntermediateAssembly(
        string buildDirectory,
        string outputDirectory,
        string assemblyName,
        string targetFramework)
    {
        var intermediateRoot = Path.Combine(buildDirectory, "obj", "Debug", targetFramework);
        var assembly = Path.Combine(intermediateRoot, $"{assemblyName}.dll");
        var deadline = Environment.TickCount64 + 5_000;
        while (!File.Exists(assembly) && Environment.TickCount64 < deadline) Thread.Sleep(20);
        if (!File.Exists(assembly))
            throw new FileNotFoundException(
                $"Script compiler did not produce intermediate assembly '{assemblyName}'.", assembly);
        Directory.CreateDirectory(outputDirectory);
        File.Copy(assembly, Path.Combine(outputDirectory, Path.GetFileName(assembly)), true);
        var symbols = Path.ChangeExtension(assembly, ".pdb");
        if (File.Exists(symbols))
            File.Copy(symbols, Path.Combine(outputDirectory, Path.GetFileName(symbols)), true);
    }

    private static IReadOnlyDictionary<string, string> CopyCompilerReferences(
        IReadOnlyDictionary<string, string> references,
        string referenceRoot,
        IDictionary<string, string> copies)
    {
        Directory.CreateDirectory(referenceRoot);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references.OrderBy(reference => reference.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.GetFullPath(reference.Value);
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    $"Compiler reference '{reference.Key}' was not found.", source);
            if (!copies.TryGetValue(source, out var destination))
            {
                destination = Path.Combine(referenceRoot, $"r{copies.Count:x4}.dll");
                File.Copy(source, destination, true);
                copies[source] = destination;
            }
            result[reference.Key] = destination;
        }
        return result;
    }

    private static void PublishCompiledAssemblies(
        ProjectWorkspace workspace,
        IReadOnlyList<CompiledProjectAssembly> compiled,
        bool publishRuntimeManifest)
    {
        var instance = EditorInstanceContext.current;
        var instanceRoot = instance?.scriptAssembliesPath ?? workspace.ScriptAssembliesPath;
        foreach (var artifact in compiled)
        {
            ScriptAssemblyStore.Publish(
                workspace, artifact.Node.Name, artifact.BuildId, artifact.AssemblyPath, instanceRoot);
            if (instance is not null) PublishSharedAssembly(workspace, artifact);
            PruneOldOutputs(instanceRoot, artifact.Node.Name, artifact.AssemblyPath);
        }

        if (!publishRuntimeManifest) return;
        PublishManifest(workspace, compiled, instanceRoot);
        if (instance is not null)
        {
            var sharedArtifacts = compiled.Select(artifact => new CompiledProjectAssembly
            {
                Node = artifact.Node,
                BuildId = artifact.BuildId,
                AssemblyPath = ScriptAssemblyStore.ResolveCurrentPath(workspace, artifact.Node.Name) ??
                               throw new FileNotFoundException(
                                   $"Shared project assembly '{artifact.Node.Name}' was not published.")
            }).ToArray();
            PublishManifest(workspace, sharedArtifacts, workspace.ScriptAssembliesPath);
        }
    }

    private static void PublishManifest(
        ProjectWorkspace workspace,
        IEnumerable<CompiledProjectAssembly> compiled,
        string scriptAssembliesRoot)
    {
        var root = Path.GetFullPath(scriptAssembliesRoot);
        var manifest = new ProjectScriptAssemblyManifestData
        {
            Assemblies = compiled.Select(artifact => new ProjectScriptAssemblyData
            {
                Assembly = artifact.Node.Name,
                BuildId = artifact.BuildId,
                RelativePath = Path.GetRelativePath(root, artifact.AssemblyPath)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                References = artifact.Node.References.ToList()
            }).ToList()
        };
        ScriptAssemblyStore.PublishProjectManifest(workspace, manifest, root);
    }

    private static void PublishSharedAssembly(
        ProjectWorkspace workspace,
        CompiledProjectAssembly artifact)
    {
        var sourceDirectory = Path.GetDirectoryName(artifact.AssemblyPath)!;
        var sharedDirectory = Path.Combine(
            ScriptAssemblyStore.GetAssemblyRoot(workspace, artifact.Node.Name), PhysicalBuildId(artifact.BuildId));
        if (!File.Exists(Path.Combine(sharedDirectory, $"{artifact.Node.Name}.dll")))
            CopyDirectory(sourceDirectory, sharedDirectory);
        var sharedAssembly = Path.Combine(sharedDirectory, $"{artifact.Node.Name}.dll");
        ScriptAssemblyStore.Publish(
            workspace, artifact.Node.Name, artifact.BuildId, sharedAssembly);
        PruneOldOutputs(workspace.ScriptAssembliesPath, artifact.Node.Name, sharedAssembly);
    }

    private static IReadOnlyList<Assembly> LoadAssemblies(
        IReadOnlyList<CompiledProjectAssembly> compiled,
        IReadOnlyDictionary<string, string> baseReferences,
        IReadOnlyDictionary<string, string> existingProjectReferences)
    {
        if (compiled.Count == 0) return [];
        var references = baseReferences.ToDictionary(
            reference => reference.Key, reference => reference.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var reference in existingProjectReferences) references[reference.Key] = reference.Value;
        foreach (var artifact in compiled) references[artifact.Node.Name] = artifact.AssemblyPath;

        Dictionary<string, Assembly> sharedProjectAssemblies;
        lock (LoadContextGate)
        {
            sharedProjectAssemblies = ActiveLoadContexts.SelectMany(context => context.Assemblies)
                .Where(assembly => assembly.GetName().Name is { } name &&
                                   existingProjectReferences.ContainsKey(name))
                .GroupBy(assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        var context = new ProjectScriptLoadContext(references, sharedProjectAssemblies);
        try
        {
            var assemblies = compiled.Select(artifact =>
                    context.LoadFromAssemblyPath(Path.GetFullPath(artifact.AssemblyPath)))
                .ToArray();
            lock (LoadContextGate) ActiveLoadContexts.Add(context);
            return assemblies;
        }
        catch
        {
            RuntimeTypeCache.UnregisterAssemblies(context.Assemblies);
            context.Unload();
            throw;
        }
    }

    private static void ReleaseLoadContexts(
        IReadOnlyCollection<ProjectScriptLoadContext> contexts,
        bool collect)
    {
        if (contexts.Count == 0) return;
        lock (LoadContextGate)
            foreach (var context in contexts) ActiveLoadContexts.Remove(context);
        foreach (var context in contexts)
        {
            RuntimeTypeCache.UnregisterAssemblies(context.Assemblies);
            context.Unload();
        }
        if (!collect) return;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string ComputeBuildId(
        ProjectWorkspace workspace,
        ProjectAssemblyNode node,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration,
        IReadOnlyDictionary<string, byte[]> sourceSnapshots)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "BEngine.ProjectAssembly.v1");
        Append(hash, node.Name);
        Append(hash, node.RootNamespace);
        Append(hash, node.AllowUnsafeCode.ToString());
        Append(hash, configuration.TargetFramework);
        Append(hash, configuration.Platform.ToString());
        foreach (var symbol in configuration.DefineSymbols) Append(hash, symbol);
        foreach (var source in node.SourcePaths)
        {
            Append(hash, Path.GetRelativePath(workspace.RootPath, source)
                .Replace(Path.DirectorySeparatorChar, '/'));
            hash.AppendData(sourceSnapshots[source]);
        }
        foreach (var reference in references.OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Key);
            hash.AppendData(File.ReadAllBytes(reference.Value));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static ScriptBuildConfiguration CreateBuildConfigurationDefaults(bool editor)
    {
        var symbols = BEngineCompilationSymbols.Create(
            editor, BEngineCompilationSymbols.DebugConfiguration);
        return new ScriptBuildConfiguration(
            editor ? "net10.0-windows" : "net10.0",
            BEngineCompilationSymbols.ResolvePlatform(editor), symbols, editor);
    }

    private static void ValidateDefineSymbol(string symbol, string source)
    {
        if (!(char.IsLetter(symbol[0]) || symbol[0] == '_') ||
            symbol.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            throw new InvalidDataException($"Invalid scripting define symbol '{symbol}' in '{source}'.");
    }

    private static string? ReadableLocation(Assembly? assembly)
    {
        if (assembly is null || assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location)) return null;
        var path = Path.GetFullPath(assembly.Location);
        return File.Exists(path) ? path : null;
    }

    private static void PruneOldOutputs(
        string scriptAssembliesRoot,
        string assemblyName,
        string currentAssemblyPath)
    {
        var assemblyRoot = ScriptAssemblyStore.GetAssemblyRoot(scriptAssembliesRoot, assemblyName);
        if (!Directory.Exists(assemblyRoot)) return;
        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(currentAssemblyPath));
        foreach (var directory in Directory.EnumerateDirectories(assemblyRoot)
                     .OrderByDescending(Directory.GetLastWriteTimeUtc)
                     .Skip(8))
        {
            if (string.Equals(Path.GetFullPath(directory), currentDirectory,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            TryDeleteDirectory(directory);
        }
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
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string DiagnosticTail(string diagnostics) => string.Join(Environment.NewLine,
        diagnostics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(20));

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static string PhysicalBuildId(string buildId) => buildId[..24];

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

}
