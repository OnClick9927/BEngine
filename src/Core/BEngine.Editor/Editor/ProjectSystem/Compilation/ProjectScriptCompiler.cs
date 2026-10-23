using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ProjectSystem;

public static class ProjectScriptCompiler
{
    private static readonly Lock LoadContextGate = new();
    private static readonly List<AssemblyScope> ActiveLoadContexts = [];

    internal static ScriptBuildConfiguration RuntimeBuildConfiguration { get; } =
        CreateBuildConfigurationDefaults(editor: false);

    public static Assembly? CompileAndLoad(ProjectWorkspace workspace)
    {
        var packages = RuntimePackageLoader.LoadEnabled(workspace);
        return CompileAndLoad(workspace, packages);
    }

    public static Assembly? CompileAndLoad(ProjectWorkspace workspace, RuntimePackageSet packages)
        => CompileAndLoad(workspace, packages, ProcessUtility.Current);

    internal static Assembly? CompileAndLoad(
        ProjectWorkspace workspace,
        RuntimePackageSet packages,
        ProcessUtility processRunner)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(processRunner);
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
            publishRuntimeManifest: true,
            processRunner);
        return result.EntryAssembly;
    }

    internal static void ReleaseLoadContexts()
    {
        AssemblyScope[] contexts;
        lock (LoadContextGate) contexts = ActiveLoadContexts.ToArray();
        ReleaseLoadContexts(contexts, collect: true);
    }

    internal static ProjectAssemblyCompilationResult CompileAndLoadGraph(
        ProjectWorkspace workspace,
        IReadOnlyList<ProjectAssemblyNode> nodes,
        IReadOnlyDictionary<string, string> baseReferences,
        IReadOnlyDictionary<string, string> existingProjectReferences,
        ScriptBuildConfiguration configuration,
        bool publishRuntimeManifest,
        ProcessUtility? processRunner = null)
    {
        var build = CompileGraph(workspace, nodes, baseReferences, existingProjectReferences,
            configuration, publishRuntimeManifest, raiseCompilationEvents: true,
            processRunner: processRunner);
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
        CancellationToken cancellationToken = default,
        ProcessUtility? processRunner = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        processRunner ??= ProcessUtility.Current;
        var availableProjectReferences = existingProjectReferences.ToDictionary(
            reference => reference.Key, reference => reference.Value, StringComparer.OrdinalIgnoreCase);
        var compiled = new List<CompiledProjectAssembly>(nodes.Count);
        var referenceRoot = EditorHostServices.Paths.Combine(EditorInstanceContext.current?.tempPath ?? workspace.TempPath,
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
                    raiseCompilationEvents, processRunner, cancellationToken);
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
        AssemblyScope[] previous;
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
            AssemblyScope[] staged;
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
        ScriptBuildConfiguration configuration,
        ProcessUtility? processRunner = null)
    {
        _ = logFileName;
        var sources = EditorHostServices.FileSystem.DirectoryExists(sourceDirectory)
            ? EditorHostServices.FileSystem.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !ProjectAssemblyDatabase.IsStreamingAssetsPath(workspace, path))
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
            publishRuntimeManifest: !configuration.IsEditor,
            processRunner).EntryAssembly;
    }

    internal static ScriptBuildConfiguration CreateBuildConfiguration(
        ProjectWorkspace workspace,
        bool editor,
        string configuration = BEngineCompilationSymbols.DebugConfiguration,
        RuntimePlatform? platform = null,
        System.Runtime.InteropServices.Architecture? architecture = null)
    {
        var symbols = BEngineCompilationSymbols.Create(
            editor, configuration, platform ?? BEngineCompilationSymbols.ResolvePlatform(editor),
            architecture ?? BEngine.Platform.PlatformUtility.Current.ProcessArchitecture).ToHashSet(StringComparer.Ordinal);
        var settingsPath = workspace.ProjectSettingsFilePath;
        if (EditorHostServices.FileSystem.FileExists(settingsPath))
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
            platform ?? BEngineCompilationSymbols.ResolvePlatform(editor),
            symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
            editor, configuration);
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
            new XElement("Optimize", configuration.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase) ? "true" : "false"),
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
        var loaded = EditorHostServices.Platform.GetLoadedAssemblies().FirstOrDefault(candidate =>
            string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (ReadableLocation(loaded) is { } loadedPath) return loadedPath;

        if (preferredAssemblyPaths is not null &&
            preferredAssemblyPaths.TryGetValue(assemblyName, out var preferredPath) &&
            EditorHostServices.FileSystem.FileExists(preferredPath))
            return EditorHostServices.Paths.GetFullPath(preferredPath);

        var applicationPath = EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, $"{assemblyName}.dll");
        if (EditorHostServices.FileSystem.FileExists(applicationPath)) return EditorHostServices.Paths.GetFullPath(applicationPath);

        if (RuntimePackageLoader.ResolveAssemblyPath(workspace, assemblyName) is { } packagePath)
            return packagePath;

        Exception? loadFailure = null;
        try
        {
            var resolved = AssemblyLoadingUtility.Current.LoadDefault(new AssemblyName(assemblyName));
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
        var fullPath = EditorHostServices.Paths.GetFullPath(assemblyPath);
        if (!EditorHostServices.FileSystem.FileExists(fullPath))
            throw new FileNotFoundException("The preferred assembly was not found.", fullPath);
        var name = ReadAssemblyName(fullPath);
        if (string.IsNullOrWhiteSpace(name))
            throw new BadImageFormatException($"The preferred assembly '{fullPath}' has no assembly name.");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = fullPath };
    }

    private static string ReadAssemblyName(string path)
    {
        using var stream = EditorHostServices.FileSystem.OpenRead(path);
        using var reader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        if (!reader.HasMetadata)
            throw new BadImageFormatException($"The preferred assembly '{path}' has no CLR metadata.");
        var metadata = reader.GetMetadataReader();
        if (!metadata.IsAssembly)
            throw new BadImageFormatException($"The preferred assembly '{path}' is not an assembly.");
        return metadata.GetString(metadata.GetAssemblyDefinition().Name);
    }

    private static CompiledProjectAssembly CompileAssembly(
        ProjectWorkspace workspace,
        ProjectAssemblyNode node,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration,
        bool raiseCompilationEvents,
        ProcessUtility processRunner,
        CancellationToken cancellationToken)
    {
        var sourceSnapshots = node.SourcePaths.ToDictionary(
            source => source,
            EditorHostServices.FileSystem.ReadAllBytes,
            StringComparer.OrdinalIgnoreCase);
        var buildId = ComputeBuildId(workspace, node, references, configuration, sourceSnapshots);
        var instance = EditorInstanceContext.current;
        var temporaryRoot = instance?.tempPath ?? workspace.TempPath;
        var scriptAssembliesRoot = instance?.scriptAssembliesPath ?? workspace.ScriptAssembliesPath;
        var logsRoot = instance?.logsPath ?? workspace.LogsPath;
        var outputDirectory = EditorHostServices.Paths.Combine(
            ScriptAssemblyStore.GetAssemblyRoot(scriptAssembliesRoot, node.Name), PhysicalBuildId(buildId));
        var assemblyPath = EditorHostServices.Paths.Combine(outputDirectory, $"{node.Name}.dll");

        if (!EditorHostServices.FileSystem.FileExists(assemblyPath) && instance is not null)
        {
            var sharedDirectory = EditorHostServices.Paths.Combine(
                ScriptAssemblyStore.GetAssemblyRoot(workspace, node.Name), PhysicalBuildId(buildId));
            var sharedAssembly = EditorHostServices.Paths.Combine(sharedDirectory, $"{node.Name}.dll");
            if (EditorHostServices.FileSystem.FileExists(sharedAssembly)) CopyDirectory(sharedDirectory, outputDirectory);
        }

        if (!EditorHostServices.FileSystem.FileExists(assemblyPath))
        {
            var buildDirectory = EditorHostServices.Paths.Combine(temporaryRoot, "ScriptBuild", PhysicalBuildId(buildId));
            EditorHostServices.FileSystem.CreateDirectory(buildDirectory);
            EditorHostServices.FileSystem.CreateDirectory(outputDirectory);
            EditorHostServices.FileSystem.CreateDirectory(logsRoot);
            var projectPath = EditorHostServices.Paths.Combine(buildDirectory, "build.csproj");
            var logPath = EditorHostServices.Paths.Combine(logsRoot, $"ScriptCompilation.{Sanitize(node.Name)}.log");
            var phaseLogPath = EditorHostServices.Paths.Combine(logsRoot,
                configuration.IsEditor ? "EditorScriptCompilation.log" : "ScriptCompilation.log");
            if (raiseCompilationEvents) CompilationPipeline.RaiseAssemblyCompilationStarted(assemblyPath);
            try
            {
                var snapshotRoot = EditorHostServices.Paths.Combine(buildDirectory, "Sources");
                var snapshotPaths = new List<string>(node.SourcePaths.Count);
                for (var index = 0; index < node.SourcePaths.Count; index++)
                {
                    var source = node.SourcePaths[index];
                    var relative = EditorHostServices.Paths.GetRelativePath(workspace.RootPath, source);
                    if (relative.StartsWith("..", StringComparison.Ordinal))
                        relative = EditorHostServices.Paths.Combine("External", $"{index:x4}-{EditorHostServices.Paths.GetFileName(source)}");
                    var snapshotPath = EditorHostServices.Paths.Combine(snapshotRoot, relative);
                    EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(snapshotPath)!);
                    var displayPath = EditorHostServices.Paths.GetRelativePath(workspace.RootPath, source)
                        .Replace(EditorHostServices.Paths.DirectorySeparatorChar, '/');
                    var loopGuarded = EditorLoopGuardInstrumenter.Instrument(
                         sourceSnapshots[source], displayPath, configuration.DefineSymbols, instrument: !configuration.IsPlayerBuild);
                    var instrumented = TestCoverageInstrumenter.Instrument(
                        loopGuarded, displayPath, configuration.DefineSymbols);
                    EditorHostServices.FileSystem.WriteAllText(snapshotPath, instrumented);
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
                Build(processRunner, workspace, projectPath, outputDirectory, logPath, phaseLogPath, node.Name,
                    configuration.TargetFramework, configuration.Configuration, cancellationToken);
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

        if (!EditorHostServices.FileSystem.FileExists(assemblyPath))
            throw new FileNotFoundException($"The {node.Name} assembly was not produced.", assemblyPath);
        return new CompiledProjectAssembly
        {
            Node = node,
            BuildId = buildId,
            AssemblyPath = assemblyPath
        };
    }

    private static void Build(
        ProcessUtility processRunner,
        ProjectWorkspace workspace,
        string projectPath,
        string outputDirectory,
        string logPath,
        string phaseLogPath,
        string assemblyName,
        string targetFramework,
        string configuration,
        CancellationToken cancellationToken)
    {
        var request = new ProcessRunRequest("dotnet")
        {
            WorkingDirectory = workspace.RootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        request.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        BEngine.Tooling.ToolEnvironment.ApplyToDotNetBuild(request.EnvironmentVariables);
        foreach (var argument in new[]
                 {
                     "build", projectPath, "--nologo", "--disable-build-servers", "-m:1", "-nr:false",
                     "--configuration", configuration, "--output", outputDirectory
                 })
            request.Arguments.Add(argument);

        var result = processRunner.Run(request, cancellationToken);
        var diagnostics = result.StandardOutput + result.StandardError;
        EditorHostServices.FileSystem.WriteAllText(logPath, diagnostics);
        EditorHostServices.FileSystem.WriteAllText(phaseLogPath, diagnostics);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"{assemblyName} failed to compile. See '{logPath}'." + EditorHostServices.Platform.NewLine +
                DiagnosticTail(diagnostics));
        CopyIntermediateAssembly(EditorHostServices.Paths.GetDirectoryName(projectPath)!, outputDirectory, assemblyName,
            targetFramework, configuration);
    }

    private static void CopyIntermediateAssembly(
        string buildDirectory,
        string outputDirectory,
        string assemblyName,
        string targetFramework,
        string configuration)
    {
        var intermediateRoot = EditorHostServices.Paths.Combine(buildDirectory, "obj", configuration, targetFramework);
        var assembly = EditorHostServices.Paths.Combine(intermediateRoot, $"{assemblyName}.dll");
        if (!EditorHostServices.FileSystem.FileExists(assembly))
            throw new FileNotFoundException(
                $"Script compiler did not produce intermediate assembly '{assemblyName}'.", assembly);
        EditorHostServices.FileSystem.CreateDirectory(outputDirectory);
        EditorHostServices.FileSystem.CopyFile(assembly, EditorHostServices.Paths.Combine(outputDirectory, EditorHostServices.Paths.GetFileName(assembly)), true);
        var symbols = EditorHostServices.ChangeExtension(assembly, ".pdb");
        if (EditorHostServices.FileSystem.FileExists(symbols))
            EditorHostServices.FileSystem.CopyFile(symbols, EditorHostServices.Paths.Combine(outputDirectory, EditorHostServices.Paths.GetFileName(symbols)), true);
    }

    private static IReadOnlyDictionary<string, string> CopyCompilerReferences(
        IReadOnlyDictionary<string, string> references,
        string referenceRoot,
        IDictionary<string, string> copies)
    {
        EditorHostServices.FileSystem.CreateDirectory(referenceRoot);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references.OrderBy(reference => reference.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            var source = EditorHostServices.Paths.GetFullPath(reference.Value);
            if (!EditorHostServices.FileSystem.FileExists(source))
                throw new FileNotFoundException(
                    $"Compiler reference '{reference.Key}' was not found.", source);
            if (!copies.TryGetValue(source, out var destination))
            {
                destination = EditorHostServices.Paths.Combine(referenceRoot, $"r{copies.Count:x4}.dll");
                EditorHostServices.FileSystem.CopyFile(source, destination, true);
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
        var root = EditorHostServices.Paths.GetFullPath(scriptAssembliesRoot);
        var manifest = new ProjectScriptAssemblyManifestData
        {
            Assemblies = compiled.Select(artifact => new ProjectScriptAssemblyData
            {
                Assembly = artifact.Node.Name,
                BuildId = artifact.BuildId,
                RelativePath = EditorHostServices.Paths.GetRelativePath(root, artifact.AssemblyPath)
                    .Replace(EditorHostServices.Paths.DirectorySeparatorChar, '/'),
                References = artifact.Node.References.ToList()
            }).ToList()
        };
        ScriptAssemblyStore.PublishProjectManifest(workspace, manifest, root);
    }

    private static void PublishSharedAssembly(
        ProjectWorkspace workspace,
        CompiledProjectAssembly artifact)
    {
        var sourceDirectory = EditorHostServices.Paths.GetDirectoryName(artifact.AssemblyPath)!;
        var sharedDirectory = EditorHostServices.Paths.Combine(
            ScriptAssemblyStore.GetAssemblyRoot(workspace, artifact.Node.Name), PhysicalBuildId(artifact.BuildId));
        if (!EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(sharedDirectory, $"{artifact.Node.Name}.dll")))
            CopyDirectory(sourceDirectory, sharedDirectory);
        var sharedAssembly = EditorHostServices.Paths.Combine(sharedDirectory, $"{artifact.Node.Name}.dll");
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
        var context = AssemblyLoadingUtility.Current.CreateProjectScope(references, sharedProjectAssemblies);
        try
        {
            var assemblies = compiled.Select(artifact =>
                    context.LoadFromAssemblyPath(EditorHostServices.Paths.GetFullPath(artifact.AssemblyPath)))
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
        IReadOnlyCollection<AssemblyScope> contexts,
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
        Append(hash, "BEngine.ProjectAssembly.v2");
        Append(hash, EditorLoopGuardInstrumenter.Revision);
        Append(hash, TestCoverageInstrumenter.Revision);
        Append(hash, node.Name);
        Append(hash, node.RootNamespace);
        Append(hash, node.AllowUnsafeCode.ToString());
        Append(hash, configuration.TargetFramework);
        Append(hash, configuration.Configuration);
        Append(hash, configuration.IsPlayerBuild.ToString());
        Append(hash, configuration.Platform.ToString());
        foreach (var symbol in configuration.DefineSymbols) Append(hash, symbol);
        foreach (var source in node.SourcePaths)
        {
            Append(hash, EditorHostServices.Paths.GetRelativePath(workspace.RootPath, source)
                .Replace(EditorHostServices.Paths.DirectorySeparatorChar, '/'));
            hash.AppendData(sourceSnapshots[source]);
        }
        foreach (var reference in references.OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Key);
            hash.AppendData(EditorHostServices.FileSystem.ReadAllBytes(reference.Value));
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
        var path = EditorHostServices.Paths.GetFullPath(assembly.Location);
        return EditorHostServices.FileSystem.FileExists(path) ? path : null;
    }

    private static void PruneOldOutputs(
        string scriptAssembliesRoot,
        string assemblyName,
        string currentAssemblyPath)
    {
        var assemblyRoot = ScriptAssemblyStore.GetAssemblyRoot(scriptAssembliesRoot, assemblyName);
        if (!EditorHostServices.FileSystem.DirectoryExists(assemblyRoot)) return;
        var currentDirectory = EditorHostServices.Paths.GetDirectoryName(EditorHostServices.Paths.GetFullPath(currentAssemblyPath));
        foreach (var directory in EditorHostServices.FileSystem.EnumerateDirectories(assemblyRoot)
                     .OrderByDescending(EditorHostServices.FileSystem.GetLastWriteTimeUtc)
                     .Skip(8))
        {
            if (string.Equals(EditorHostServices.Paths.GetFullPath(directory), currentDirectory,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            TryDeleteDirectory(directory);
        }
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
            EditorHostServices.FileSystem.CopyFile(file, target, overwrite: true);
        }
    }

    private static string DiagnosticTail(string diagnostics) => string.Join(EditorHostServices.Platform.NewLine,
        diagnostics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(20));

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        EditorHostServices.Paths.GetInvalidFileNameChars().Contains(character) ? '_' : character));

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
            if (EditorHostServices.FileSystem.DirectoryExists(path)) EditorHostServices.FileSystem.DeleteDirectory(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

}
