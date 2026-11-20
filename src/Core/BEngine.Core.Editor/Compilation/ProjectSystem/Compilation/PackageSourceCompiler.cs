using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;

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
        ScriptBuildConfiguration? configurationOverride = null,
        CancellationToken cancellationToken = default,
        bool includeEditorAssemblies = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(processRunner);

        var selectedDefinitions = definitions.ToArray();
        var nodes = BuildIndex1368(CreateNodes(selectedDefinitions).Where(node => includeEditorAssemblies || node.Kind == "runtime"), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> BuildIndex1368(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> indexSource1368, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer1368)
        {
            ArgumentNullException.ThrowIfNull(indexSource1368, "source");
            var knownCount = indexSource1368.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>(indexComparer1368);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> values;
            if (knownCount && indexSource1368 is global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[] array)
                values = array;
            else if (knownCount && indexSource1368 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>(capacity, indexComparer1368);
                foreach (var item in indexSource1368)
                    result.Add(IndexKey1368(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>(values.Length, indexComparer1368);
                foreach (var item in values)
                    result.Add(IndexKey1368(item), item);
                return result;
            }
        
            string IndexKey1368(global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode node) => node.Key;
        }
        ValidateAssemblyDefinitions(nodes.Values);
        _ = PackageAssemblyPlan.Create(selectedDefinitions, includeEditorAssemblies);
        var remaining = new Dictionary<string, CompilationNode>(nodes, StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        using var referenceSnapshot = new CompilerReferenceSnapshot();
        var configurations = BuildIndex2004(nodes.Values.Select(node => node.Kind).Distinct(StringComparer.Ordinal), StringComparer.Ordinal);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.ScriptBuildConfiguration> BuildIndex2004(global::System.Collections.Generic.IEnumerable<string> indexSource2004, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2004)
        {
            ArgumentNullException.ThrowIfNull(indexSource2004, "source");
            var knownCount = indexSource2004.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.ScriptBuildConfiguration>(indexComparer2004);
            global::System.ReadOnlySpan<string> values;
            if (knownCount && indexSource2004 is string[] array)
                values = array;
            else if (knownCount && indexSource2004 is global::System.Collections.Generic.List<string> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.ScriptBuildConfiguration>(capacity, indexComparer2004);
                foreach (var item in indexSource2004)
                    result.Add(IndexKey2004(item), IndexValue2004(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.ScriptBuildConfiguration>(values.Length, indexComparer2004);
                foreach (var item in values)
                    result.Add(IndexKey2004(item), IndexValue2004(item));
                return result;
            }
        
            string IndexKey2004(string kind) => kind;
            global::BEngine.ProjectSystem.ScriptBuildConfiguration IndexValue2004(string kind) => configurationOverride ?? ProjectScriptCompiler.CreateBuildConfiguration(workspace, kind == "editor");
        }

        while (remaining.Count > 0)
        {
            var phase = Matches2344() ? "runtime" : "editor";
            [global::BEngine.SkipLoopGuard]
            bool Matches2344()
            {
                var matchSource2344 = remaining.Values;
                ArgumentNullException.ThrowIfNull(matchSource2344, "source");
                global::System.ReadOnlySpan<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> matchValues2344;
                if (matchSource2344.GetType() == typeof(global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]))
                {
                    matchValues2344 = (global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[])(object)matchSource2344;
                }
                else if (matchSource2344.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>))
                {
                    matchValues2344 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>)(object)matchSource2344);
                }
                else
                {
                    foreach (var node in (global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>)matchSource2344)
                    {
                        if (node.Kind == "runtime")
                            return true;
                    }
            
                    return false;
                }
            
                foreach (var node in matchValues2344)
                {
                    if (node.Kind == "runtime")
                        return true;
                }
            
                return false;
            }
            var ready = remaining.Values
                .Where(node => node.Kind == phase)
                .Where(node => Dependencies(node, nodes).All(dependency => completed.Contains(dependency.Key)))
                .OrderBy(node => node.Definition.Document.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Kind, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
                throw new InvalidDataException("Package source dependency cycle prevents compilation.");

            // Dependencies completed in earlier waves are immutable while this
            // wave builds. Each node owns a distinct source/output directory.
            var outputs = EditorWorkBatch.Run(ready.Length, readyIndex =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = ready[readyIndex];
                if (node.Assembly.Bootstrap) return ResolveBootstrapAssembly(node);
                var references = CreateReferences(node, nodes, paths);
                return CompileNode(workspace, node, references, processRunner, isolated, configurations[node.Kind], referenceSnapshot);
            }, (done, readyIndex) => EditorCallbackDispatcher.Invoke(progress, index + done, nodes.Count,
                $"{ready[readyIndex].Definition.Document.DisplayName} ({ready[readyIndex].Kind})",
                "PackageSourceCompiler.progress"), cancellationToken);
            for (var readyIndex = 0; readyIndex < ready.Length; readyIndex++)
            {
                var node = ready[readyIndex];
                paths[node.Assembly.Assembly] = outputs[readyIndex];
                completed.Add(node.Key);
                remaining.Remove(node.Key);
                index++;
            }
        }

        EditorCallbackDispatcher.Invoke(progress, nodes.Count, nodes.Count,
            "瀹屾垚", "PackageSourceCompiler.progress");
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
        var nodesByAssembly = BuildIndex9022(nodes.GroupBy(node => node.AssemblyDefinition.Name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]> BuildIndex9022(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>> indexSource9022, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer9022)
        {
            ArgumentNullException.ThrowIfNull(indexSource9022, "source");
            var knownCount = indexSource9022.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>(indexComparer9022);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>> values;
            if (knownCount && indexSource9022 is global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>[] array)
                values = array;
            else if (knownCount && indexSource9022 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>(capacity, indexComparer9022);
                foreach (var item in indexSource9022)
                    result.Add(IndexKey9022(item), IndexValue9022(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>(values.Length, indexComparer9022);
                foreach (var item in values)
                    result.Add(IndexKey9022(item), IndexValue9022(item));
                return result;
            }
        
            string IndexKey9022(global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> group) => group.Key;
            global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[] IndexValue9022(global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> group) => group.ToArray();
        }
        var duplicate = Matches7579();
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]> Matches7579()
        {
            var matchSource7579 = nodesByAssembly;
            ArgumentNullException.ThrowIfNull(matchSource7579, "source");
            global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>> matchValues7579;
            if (matchSource7579.GetType() == typeof(global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>[]))
            {
                matchValues7579 = (global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>[])(object)matchSource7579;
            }
            else if (matchSource7579.GetType() == typeof(global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>>))
            {
                matchValues7579 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>>)(object)matchSource7579);
            }
            else
            {
                foreach (var group in (global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]>>)matchSource7579)
                {
                    if (group.Value.Length != 1)
                        return group;
                }
        
                return default!;
            }
        
            foreach (var group in matchValues7579)
            {
                if (group.Value.Length != 1)
                    return group;
            }
        
            return default!;
        }
        if (duplicate.Value is { Length: > 1 })
            throw new InvalidDataException(
                $"Package assembly definition name '{duplicate.Key}' is declared more than once: " +
                string.Join(", ", duplicate.Value.Select(node => node.AssemblyDefinitionPath)));

        var nodesByKey = BuildIndex11666(nodes, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> BuildIndex11666(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> indexSource11666, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer11666)
        {
            ArgumentNullException.ThrowIfNull(indexSource11666, "source");
            var knownCount = indexSource11666.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>(indexComparer11666);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> values;
            if (knownCount && indexSource11666 is global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[] array)
                values = array;
            else if (knownCount && indexSource11666 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>(capacity, indexComparer11666);
                foreach (var item in indexSource11666)
                    result.Add(IndexKey11666(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>(values.Length, indexComparer11666);
                foreach (var item in values)
                    result.Add(IndexKey11666(item), item);
                return result;
            }
        
            string IndexKey11666(global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode node) => node.Key;
        }
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
            ["BEngine.Core"] = typeof(BEngine.DependencyInjection.IEngineServiceModule).Assembly.Location,
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] =
                typeof(IServiceCollection).Assembly.Location
        };
        CoreModuleReferences.AddRuntime(references);
        if (node.Kind == "editor")
        {
            references["BEngine.Core.Editor"] = typeof(BPackageDefinition).Assembly.Location;
            CoreModuleReferences.AddEditor(references);
        }

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
            bool Query11660()
            {
                var querySource11660 = nodes.Values;
                ArgumentNullException.ThrowIfNull(querySource11660, "source");
                if (querySource11660.GetType() == typeof(global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]) || querySource11660.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>))
                {
                    global::System.ReadOnlySpan<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode> queryValues11660 = querySource11660.GetType() == typeof(global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[]) ? (global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode[])(object)querySource11660 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageSourceCompiler.CompilationNode>)(object)querySource11660);
                    foreach (var n in queryValues11660)
                    {
                        if (n.Assembly.Assembly.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                else
                {
                    foreach (var n in querySource11660)
                    {
                        if (n.Assembly.Assembly.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(name) || name.AsSpan(0, name.Length).IndexOfAny('/', '\\', ':') >= 0 || Query11660())
                throw new InvalidDataException($"Invalid host reference '{name}'; package assemblies require package dependencies.");
            var location = EditorHostServices.Paths.Combine(EditorHostServices.Paths.GetDirectoryName(typeof(BEngine.Editor.EditorServices).Assembly.Location)!, name + ".dll");
            if (!EditorHostServices.FileSystem.FileExists(location)) throw new FileNotFoundException($"Host assembly '{name}' is unavailable.", location);
            if (node.Kind == "runtime" && ReferencesEditorHost(location, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Runtime assembly '{node.Assembly.Assembly}' cannot reference Editor host '{name}'.");
            references[name] = location;
        }
        return references;
    }

    private static string ResolveBootstrapAssembly(CompilationNode node)
    {
        var root = EditorHostServices.Paths.GetDirectoryName(typeof(EditorServices).Assembly.Location)!;
        var path = EditorHostServices.Paths.Combine(root, node.Assembly.Assembly + ".dll");
        if (!EditorHostServices.FileSystem.FileExists(path))
            throw new FileNotFoundException($"Bootstrap assembly '{node.Assembly.Assembly}' is missing. Repair or update this engine installation.", path);
        var identity = System.Reflection.AssemblyName.GetAssemblyName(path);
        if (!string.Equals(identity.Name, node.Assembly.Assembly, StringComparison.Ordinal))
            throw new InvalidDataException($"Bootstrap assembly identity does not match '{node.Assembly.Assembly}'.");
        return path;
    }

    private static bool ReferencesEditorHost(string path, ISet<string> visited)
    {
        if (!visited.Add(path)) return false;
        var editorName = typeof(BEngine.Editor.EditorServices).Assembly.GetName().Name!;
        using var stream = EditorHostServices.FileSystem.OpenRead(path);
        using var reader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var metadata = reader.GetMetadataReader();
        if (metadata.GetString(metadata.GetAssemblyDefinition().Name).Equals(editorName, StringComparison.OrdinalIgnoreCase))
            return true;
        var root = EditorHostServices.Paths.GetDirectoryName(path)!;
        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (name.Equals(editorName, StringComparison.OrdinalIgnoreCase)) return true;
            var nested = EditorHostServices.Paths.Combine(root, name + ".dll");
            if (EditorHostServices.FileSystem.FileExists(nested) && ReferencesEditorHost(nested, visited)) return true;
        }
        return false;
    }

    private static string CompileNode(
        ProjectWorkspace workspace,
        CompilationNode node,
        IReadOnlyDictionary<string, string> references,
        ProcessUtility processRunner,
        bool isolated,
        ScriptBuildConfiguration configuration,
        CompilerReferenceSnapshot referenceSnapshot)
    {
        using var measurement = EditorStartupTimings.Measure($"Package cache/build: {node.Key}");
        var sourceRoot = node.SourceRoot;
        var sources = EditorHostServices.FileSystem.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsBuildDirectory(sourceRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
            throw new InvalidDataException(
                $"Package '{node.Definition.Document.Id}' {node.Kind} assembly " +
                $"'{node.Assembly.Assembly}' contains no C# source files.");

        // The bytes defining this version must also be the bytes compiled.
        // Keep one node-local snapshot instead of reading every source again
        // after a cache miss, when a concurrent edit could change its contents.
        var sourceSnapshots = sources.ToDictionary(source => source,
            EditorHostServices.FileSystem.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

        string buildId;
        using (EditorStartupTimings.Measure($"Package content identity: {node.Key}"))
            buildId = ComputeBuildId(node, sourceRoot, sources, references, configuration, referenceSnapshot, sourceSnapshots);
        var instance = isolated ? null : EditorInstanceContext.current;
        // Package outputs are content-addressed and are never loaded in place.
        // The loader owns a separate shadow copy, so reopening an editor need not
        // copy cached compiler outputs into another instance-specific directory.
        var scriptAssembliesRoot = configuration.UseSharedCache
            ? workspace.ScriptAssembliesPath : instance?.scriptAssembliesPath ?? workspace.ScriptAssembliesPath;
        var outputDirectory = EditorHostServices.Paths.Combine(
            ScriptAssemblyStore.GetAssemblyRoot(scriptAssembliesRoot, node.Assembly.Assembly),
            PhysicalBuildId(buildId));
        var assemblyPath = EditorHostServices.Paths.Combine(outputDirectory, $"{node.Assembly.Assembly}.dll");

        if (!EditorHostServices.FileSystem.FileExists(assemblyPath) && instance is not null && !configuration.UseSharedCache)
        {
            var sharedDirectory = EditorHostServices.Paths.Combine(
                ScriptAssemblyStore.GetAssemblyRoot(workspace, node.Assembly.Assembly),
                PhysicalBuildId(buildId));
            var sharedAssembly = EditorHostServices.Paths.Combine(sharedDirectory, $"{node.Assembly.Assembly}.dll");
            if (EditorHostServices.FileSystem.FileExists(sharedAssembly)) CopyDirectory(sharedDirectory, outputDirectory);
        }

        if (!EditorHostServices.FileSystem.FileExists(assemblyPath))
        {
            if (!configuration.UseSharedCache)
                Build(processRunner, workspace, node, sources, references, configuration, outputDirectory, buildId, isolated, referenceSnapshot, sourceSnapshots);
            else
            {
                // Publish the complete version at once. A command-line build may
                // prepare the same project while its editor is opening.
                var stagingDirectory = EditorHostServices.Paths.Combine(
                    EditorHostServices.Paths.GetDirectoryName(outputDirectory)!, ".build-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Build(processRunner, workspace, node, sources, references, configuration,
                        stagingDirectory, buildId, isolated, referenceSnapshot, sourceSnapshots);
                    ValidateAssembly(EditorHostServices.Paths.Combine(stagingDirectory,
                        $"{node.Assembly.Assembly}.dll"), node.Assembly.Assembly);
                    try { PublishBuildDirectory(stagingDirectory, outputDirectory); }
                    catch (IOException) when (EditorHostServices.FileSystem.FileExists(assemblyPath))
                    {
                        // Another completed build won publication. Its content
                        // identity is identical; never overwrite its live files.
                        ValidateAssembly(assemblyPath, node.Assembly.Assembly);
                    }
                }
                finally { TryDeleteDirectory(stagingDirectory); }
            }
        }

        if (configuration.PublishSharedReferences)
        {
            using var publication = EditorStartupTimings.Measure($"Package reference publication: {node.Key}");
            // Validate the cached PE even when its reference document is already
            // current. A valid reference must not hide a damaged/wrong DLL.
            ValidateAssembly(assemblyPath, node.Assembly.Assembly);
            if (!SharedReferenceMatches(workspace, node.Assembly.Assembly, buildId, assemblyPath))
                ScriptAssemblyStore.Publish(workspace, node.Assembly.Assembly, buildId, assemblyPath);
        }
        return assemblyPath;
    }

    private static void PublishBuildDirectory(string source, string destination)
    {
        // Windows scanners and recently exited compiler workers can briefly
        // retain a handle. Keep publication atomic and retry only sharing locks.
        for (var attempt = 0; ; attempt++)
        {
            try { EditorHostServices.FileSystem.MoveDirectory(source, destination); return; }
            catch (IOException exception) when (attempt < 7 &&
                (exception.HResult & 0xffff) is 5 or 32 or 33 &&
                !EditorHostServices.FileSystem.DirectoryExists(destination))
            {
                Thread.Sleep(25 << attempt);
            }
        }
    }

    private static void ValidateAssembly(string path, string expectedName)
    {
        var actualName = AssemblyLoadingUtility.Current.GetAssemblyName(path).Name;
        if (!string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Script assembly '{path}' declares '{actualName}', expected '{expectedName}'.");
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
        bool isolated,
        CompilerReferenceSnapshot referenceSnapshot,
        IReadOnlyDictionary<string, byte[]> sourceSnapshots)
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
                workspace, node.SourceRoot, sources, snapshotRoot, configuration.DefineSymbols, configuration.InstrumentLoops, sourceSnapshots);
            // MSBuild's reference resolver can reject long Windows HintPaths even though
            // the file APIs used by the editor can read the same assembly. Keep compiler
            // inputs close to the generated project, as the project-script compiler does.
            var compilerReferences = CopyCompilerReferences(references,
                EditorHostServices.Paths.Combine(buildDirectory, "References"), referenceSnapshot);
            WriteProject(projectPath, node, instrumentedSources, compilerReferences, configuration,
                // Generated SDK sources and the PDB output also carry physical paths.
                // Freeze those identities across exclusive editor/tool instances while
                // retaining the original source paths for debugger navigation.
                $"{snapshotRoot}={node.SourceRoot},{buildDirectory}=/__bengine_package_build__,{outputDirectory}=/__bengine_package_output__");
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
        var metadataItems = new XElement("ItemGroup", node.Assembly.Aliases
            .OrderBy(alias => alias, StringComparer.Ordinal)
            .Select(alias => new XElement("AssemblyAttribute",
                new XAttribute("Include", "BEngine.RuntimeAssemblyAliasAttribute"),
                new XElement("_Parameter1", alias))));
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            propertyGroup, sourceItems, referenceItems, packageItems, metadataItems)).Save(path);
    }

    private static IReadOnlyDictionary<string, string> CopyCompilerReferences(
        IReadOnlyDictionary<string, string> references, string referenceRoot, CompilerReferenceSnapshot referenceSnapshot)
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
                referenceSnapshot.CopyTo(source, destination);
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
        ScriptBuildConfiguration configuration,
        CompilerReferenceSnapshot referenceSnapshot,
        IReadOnlyDictionary<string, byte[]> sourceSnapshots)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "BEngine.PackageSource.v20");
        Append(hash, EditorLoopGuardInstrumenter.Revision);
        Append(hash, node.Definition.Document.Id);
        Append(hash, node.Definition.Document.PackageVersion);
        Append(hash, node.Kind);
        Append(hash, node.Assembly.Assembly);
        Append(hash, node.Assembly.RootNamespace);
        foreach (var alias in node.Assembly.Aliases.OrderBy(alias => alias, StringComparer.Ordinal))
            Append(hash, alias);
        Append(hash, configuration.TargetFramework);
        Append(hash, configuration.Configuration);
        Append(hash, configuration.InstrumentLoops.ToString());
        Append(hash, configuration.PublishSharedReferences.ToString());
        Append(hash, configuration.UseSharedCache.ToString());
        Append(hash, configuration.Platform.ToString());
        foreach (var symbol in configuration.DefineSymbols) Append(hash, symbol);
        hash.AppendData(EditorHostServices.FileSystem.ReadAllBytes(node.AssemblyDefinitionPath));
        foreach (var source in sources)
        {
            Append(hash, EditorHostServices.Paths.GetRelativePath(sourceRoot, source).Replace(EditorHostServices.Paths.DirectorySeparatorChar, '/'));
            hash.AppendData(sourceSnapshots[source]);
        }
        foreach (var reference in references.OrderBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Key);
            referenceSnapshot.AppendTo(hash, reference.Value);
        }
        foreach (var reference in node.Assembly.PackageReferences
                     .OrderBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Id);
            Append(hash, reference.Version);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static IReadOnlyList<string> SnapshotSources(
        ProjectWorkspace workspace,
        string sourceRoot,
        IEnumerable<string> sources,
        string snapshotRoot,
        IReadOnlyList<string> preprocessorSymbols,
        bool instrument,
        IReadOnlyDictionary<string, byte[]> sourceSnapshots)
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
                sourceSnapshots[source], displayPath, preprocessorSymbols, instrument);
            EditorHostServices.FileSystem.WriteAllText(destination, instrumented);
            snapshots.Add(destination);
        }
        return snapshots;
    }

    private static bool SharedReferenceMatches(ProjectWorkspace workspace, string assemblyName,
        string buildId, string assemblyPath)
    {
        var referencePath = ScriptAssemblyStore.GetReferencePath(workspace, assemblyName);
        if (!EditorHostServices.FileSystem.FileExists(referencePath)) return false;
        try
        {
            var reference = YamlUtility.Load<ScriptAssemblyReferenceData>(referencePath,
                EditorHostServices.FileSystem, EditorHostServices.Paths);
            return reference.Format == "BEngine.ScriptAssemblyReference" && reference.Version == 1 &&
                reference.Assembly == assemblyName && reference.BuildId == buildId &&
                reference.RelativePath == EditorHostServices.Paths.GetRelativePath(
                    workspace.ScriptAssembliesPath, assemblyPath).Replace(
                    EditorHostServices.Paths.DirectorySeparatorChar, '/');
        }
        catch (Exception error) when (error is IOException or InvalidDataException or FormatException or ArgumentException)
        { return false; }
    }

    private static void CopyDirectory(string source, string destination)
    {
        using var measurement = EditorStartupTimings.Measure("Package compiled output copy");
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

    private static bool ContainsBuildDirectory(string root, string path)
    {
        var paths = EditorHostServices.Paths;
        var relative = paths.GetRelativePath(root, path).AsSpan();
        foreach (var range in relative.SplitAny([paths.DirectorySeparatorChar, paths.AltDirectorySeparatorChar]))
            if (relative[range].Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                relative[range].Equals("obj", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

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
