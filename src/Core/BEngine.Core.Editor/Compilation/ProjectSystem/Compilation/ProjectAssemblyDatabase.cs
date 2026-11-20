using BEngine.Serialization;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem.Editor;

public sealed class ProjectAssemblyDatabase
{
    private readonly ProjectWorkspace _workspace;

    public ProjectAssemblyDatabase(ProjectWorkspace workspace) =>
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public IReadOnlyList<ProjectAssemblyDefinition> Discover() => Discover(default);

    private IReadOnlyList<ProjectAssemblyDefinition> Discover(CancellationToken cancellationToken)
    {
        return _workspace.AssetRoots.SelectMany(root =>
                EditorHostServices.FileSystem.EnumerateFiles(root, "*.asmdef.yaml", SearchOption.AllDirectories))
            .Where(path => !IsStreamingAssetsPath(_workspace, path) && !IsBuildArtifact(_workspace, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ProjectAssemblyDefinition(path, Load(path));
            })
            .ToArray();
    }

    public ProjectAssemblyDefinition? FindForSource(string sourcePath)
    {
        return new SourceOwnerIndex(Discover()).Find(sourcePath);
    }

    // An invocation owns this snapshot. A later build always discovers fresh
    // files and parses definitions again, including timestamp-preserving edits.
    internal SourceSnapshot CaptureSources(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = Discover(cancellationToken);
        var owners = new SourceOwnerIndex(definitions);
        var editorRoot = EditorHostServices.Paths.TrimEndingDirectorySeparator(
            EditorHostServices.Paths.GetFullPath(_workspace.EditorScriptsPath)) + EditorHostServices.Paths.DirectorySeparatorChar;
        var sources = new List<SourceEntry>();
        foreach (var path in _workspace.AssetRoots.SelectMany(root =>
                     EditorHostServices.FileSystem.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                 .Where(path => !IsStreamingAssetsPath(_workspace, path) && !IsBuildArtifact(_workspace, path))
                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = EditorHostServices.Paths.GetFullPath(path);
            sources.Add(new SourceEntry(path, owners.Find(fullPath),
                fullPath.StartsWith(editorRoot, StringComparison.OrdinalIgnoreCase)));
        }
        return new SourceSnapshot(_workspace.RootPath, definitions, sources.ToArray());
    }

    internal sealed record SourceSnapshot(string WorkspaceRoot,
        IReadOnlyList<ProjectAssemblyDefinition> Definitions, IReadOnlyList<SourceEntry> Sources);
    internal readonly record struct SourceEntry(string Path, ProjectAssemblyDefinition? Owner, bool IsEditor);

    internal ProjectAssemblyGraph BuildGraph(
        RuntimePlatform platform,
        IReadOnlySet<string> defineSymbols,
        IReadOnlySet<string> externalAssemblies,
        bool includeEditorAssemblies,
        IReadOnlySet<string>? externalEditorAssemblies = null,
        SourceSnapshot? sourceSnapshot = null,
        CancellationToken cancellationToken = default) => BuildGraph(
        platform, defineSymbols, platform, defineSymbols, externalAssemblies, includeEditorAssemblies,
        externalEditorAssemblies, sourceSnapshot, cancellationToken);

    internal ProjectAssemblyGraph BuildGraph(
        RuntimePlatform runtimePlatform,
        IReadOnlySet<string> runtimeDefineSymbols,
        RuntimePlatform editorPlatform,
        IReadOnlySet<string> editorDefineSymbols,
        IReadOnlySet<string> externalAssemblies,
        bool includeEditorAssemblies,
        IReadOnlySet<string>? externalEditorAssemblies = null,
        SourceSnapshot? sourceSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeDefineSymbols);
        ArgumentNullException.ThrowIfNull(editorDefineSymbols);
        ArgumentNullException.ThrowIfNull(externalAssemblies);
        cancellationToken.ThrowIfCancellationRequested();
        sourceSnapshot ??= CaptureSources(cancellationToken);
        if (!string.Equals(sourceSnapshot.WorkspaceRoot, _workspace.RootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Assembly source snapshot belongs to another workspace.", nameof(sourceSnapshot));
        var definitions = sourceSnapshot.Definitions;
        ValidateUniqueNames(definitions, externalAssemblies);

        var activeDefinitions = definitions.Where(definition =>
            {
                var editorOnly = IsEditorDefinition(definition);
                if (editorOnly && !includeEditorAssemblies) return false;
                return MatchesPlatform(definition, editorOnly ? editorPlatform : runtimePlatform) &&
                       MatchesDefineConstraints(definition,
                           editorOnly ? editorDefineSymbols : runtimeDefineSymbols);
            })
            .ToArray();
        var activePaths = BuildIndex5265(activeDefinitions);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.HashSet<string> BuildIndex5265(global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition[] indexSource5265)
        {
            ArgumentNullException.ThrowIfNull(indexSource5265, "source");
            var indexComparer5265 = StringComparer.OrdinalIgnoreCase;
            var result = new global::System.Collections.Generic.HashSet<string>(indexComparer5265);
            foreach (var definition in indexSource5265)
                result.Add(definition.Path);
            return result;
        }
        var sourcesByDefinition = BuildIndex5415(activeDefinitions, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>> BuildIndex5415(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition> indexSource5415, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer5415)
        {
            ArgumentNullException.ThrowIfNull(indexSource5415, "source");
            var knownCount = indexSource5415.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>>(indexComparer5415);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition> values;
            if (knownCount && indexSource5415 is global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition[] array)
                values = array;
            else if (knownCount && indexSource5415 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>>(capacity, indexComparer5415);
                foreach (var item in indexSource5415)
                    result.Add(IndexKey5415(item), IndexValue5415(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>>(values.Length, indexComparer5415);
                foreach (var item in values)
                    result.Add(IndexKey5415(item), IndexValue5415(item));
                return result;
            }
        
            string IndexKey5415(global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition definition) => definition.Path;
            global::System.Collections.Generic.List<string> IndexValue5415(global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition _) => new List<string>();
        }
        var unownedRuntimeSources = new List<string>();
        var unownedEditorSources = new List<string>();

        foreach (var source in sourceSnapshot.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = source.Path;
            var owner = source.Owner;
            if (owner is not null)
            {
                if (activePaths.Contains(owner.Path)) sourcesByDefinition[owner.Path].Add(sourcePath);
                continue;
            }

            if (source.IsEditor)
                unownedEditorSources.Add(sourcePath);
            else
                unownedRuntimeSources.Add(sourcePath);
        }

        var projectionSource6300 = activeDefinitions;
        ArgumentNullException.ThrowIfNull(projectionSource6300, "source");
        var projectionResult6300 = new global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode>(projectionSource6300.Length);
        for (var projectionIndex6300 = 0; projectionIndex6300 < projectionSource6300.Length; projectionIndex6300++)
        {
            var projectionItem6300 = projectionSource6300[projectionIndex6300];
            projectionResult6300.Add(new ProjectAssemblyNode { Name = projectionItem6300.Document.Name, RootNamespace = projectionItem6300.Document.RootNamespace, DefinitionPath = projectionItem6300.Path, SourcePaths = sourcesByDefinition[projectionItem6300.Path], References = NormalizeReferences(projectionItem6300), EditorOnly = IsEditorDefinition(projectionItem6300), AutoReferenced = projectionItem6300.Document.AutoReferenced, AllowUnsafeCode = projectionItem6300.Document.AllowUnsafeCode, Definition = projectionItem6300.Document });
        }
        
        var nodes = projectionResult6300;

        AddFallbackAssemblies(nodes, unownedRuntimeSources, unownedEditorSources, includeEditorAssemblies);
        ValidateReferences(nodes, definitions, activeDefinitions, externalAssemblies, externalEditorAssemblies);
        return new ProjectAssemblyGraph(TopologicalSort(nodes));
    }

    internal bool MatchesBuildConfiguration(ProjectAssemblyDefinition definition, ScriptBuildConfiguration runtime,
        ScriptBuildConfiguration editor, bool includeEditor)
    {
        var editorOnly = IsEditorDefinition(definition);
        if (editorOnly && !includeEditor) return false;
        var configuration = editorOnly ? editor : runtime;
        return MatchesPlatform(definition, configuration.Platform) &&
            MatchesDefineConstraints(definition, configuration.DefineSymbols.ToHashSet(StringComparer.Ordinal));
    }

    private bool IsEditorDefinition(ProjectAssemblyDefinition definition) =>
        definition.Document.EditorOnly || IsInsideDirectory(_workspace.EditorScriptsPath, definition.Path);

    internal static bool IsStreamingAssetsPath(ProjectWorkspace workspace, string path)
    {
        var relative = EditorHostServices.Paths.GetRelativePath(
            workspace.AssetsPath, EditorHostServices.Paths.GetFullPath(path)).Replace('\\', '/');
        return relative.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuildArtifact(ProjectWorkspace workspace, string path) =>
        EditorHostServices.Paths.GetRelativePath(workspace.AssetsPath, path).Split('/', '\\')
            .Any(part => part is "bin" or "obj" or ".git" or ".vs");

    private static AssemblyDefinitionDocument Load(string path)
    {
        var document = YamlUtility.Load<AssemblyDefinitionDocument>(path);
        if (document.Format != "BEngine.AssemblyDefinition" || document.Version != 1 ||
            string.IsNullOrWhiteSpace(document.Name) || string.IsNullOrWhiteSpace(document.RootNamespace))
        {
            throw new InvalidDataException($"Invalid assembly definition: {path}");
        }
        ValidateAssemblyName(document.Name, path);
        return document;
    }

    private sealed class SourceOwnerIndex
    {
        private readonly Dictionary<string, ProjectAssemblyDefinition> _directories = new(StringComparer.OrdinalIgnoreCase);

        internal SourceOwnerIndex(IEnumerable<ProjectAssemblyDefinition> definitions)
        {
            // Several definitions in one directory retain the existing ordinal
            // path tie-breaker. Name validation still rejects duplicate names.
            foreach (var definition in definitions.OrderBy(definition => definition.Path, StringComparer.OrdinalIgnoreCase))
                _directories.TryAdd(EditorHostServices.Paths.TrimEndingDirectorySeparator(
                    EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.GetDirectoryName(definition.Path)!)), definition);
        }

        internal ProjectAssemblyDefinition? Find(string sourcePath)
        {
            for (var directory = EditorHostServices.Paths.GetDirectoryName(EditorHostServices.Paths.GetFullPath(sourcePath));
                 directory is not null; directory = EditorHostServices.Paths.GetDirectoryName(directory))
                if (_directories.TryGetValue(EditorHostServices.Paths.TrimEndingDirectorySeparator(directory), out var owner))
                    return owner;
            return null;
        }
    }

    private static void ValidateUniqueNames(
        IReadOnlyList<ProjectAssemblyDefinition> definitions,
        IReadOnlySet<string> externalAssemblies)
    {
        foreach (var group in definitions.GroupBy(
                     definition => definition.Document.Name, StringComparer.OrdinalIgnoreCase))
        {
            var matches = group.ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException(
                    $"Duplicate assembly definition name '{matches[0].Document.Name}': " +
                    string.Join(", ", matches.Select(match => match.Path)));
            if (externalAssemblies.Contains(matches[0].Document.Name))
                throw new InvalidDataException(
                    $"Assembly definition '{matches[0].Document.Name}' conflicts with an engine or package assembly: " +
                    matches[0].Path);
        }
    }

    private static IReadOnlyList<string> NormalizeReferences(ProjectAssemblyDefinition definition)
    {
        var references = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in definition.Document.References ?? [])
        {
            var name = reference?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException(
                    $"Assembly definition '{definition.Document.Name}' contains an empty reference: " +
                    definition.Path);
            if (!seen.Add(name))
                throw new InvalidDataException(
                    $"Assembly definition '{definition.Document.Name}' contains duplicate reference '{name}': " +
                    definition.Path);
            // Merged package assemblies retain their public namespaces. Resolve old
            // project asmdef references without requiring a second compatibility DLL.
            name = RuntimeTypeCache.ResolveAssemblyAlias(name);
            if (!references.Contains(name, StringComparer.OrdinalIgnoreCase)) references.Add(name);
        }
        return references;
    }

    private static void AddFallbackAssemblies(
        ICollection<ProjectAssemblyNode> nodes,
        IReadOnlyList<string> runtimeSources,
        IReadOnlyList<string> editorSources,
        bool includeEditorAssemblies)
    {
        var runtimeAutoReferences = nodes.Where(node => !node.EditorOnly && node.AutoReferenced)
            .Select(node => node.Name).ToArray();
        if (runtimeSources.Count > 0)
        {
            EnsureFallbackNameAvailable(nodes, "GameScripts");
            nodes.Add(new ProjectAssemblyNode
            {
                Name = "GameScripts",
                RootNamespace = "Game",
                DefinitionPath = string.Empty,
                SourcePaths = runtimeSources,
                References = runtimeAutoReferences,
                EditorOnly = false,
                AutoReferenced = true,
                AllowUnsafeCode = false
            });
        }

        if (!includeEditorAssemblies || editorSources.Count == 0) return;
        EnsureFallbackNameAvailable(nodes, "GameEditorScripts");
        var references = ((((global::System.Collections.Generic.IEnumerable<string>)(nodes.Where(node => node.AutoReferenced).Select(node => node.Name))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems16078, distinctComparer16078) => distinctItems16078.ToHashSet<string>(distinctComparer16078).ToArray()});
        nodes.Add(new ProjectAssemblyNode
        {
            Name = "GameEditorScripts",
            RootNamespace = "Game.Editor",
            DefinitionPath = string.Empty,
            SourcePaths = editorSources,
            References = references,
            EditorOnly = true,
            AutoReferenced = true,
            AllowUnsafeCode = false
        });
    }

    private static void EnsureFallbackNameAvailable(
        IEnumerable<ProjectAssemblyNode> nodes,
        string assemblyName)
    {
        bool __queryResult13637;
        {
            var __querySource13637 = nodes;
            ArgumentNullException.ThrowIfNull(__querySource13637, "source");
            __queryResult13637 = false;
            var __querySource13637Type = __querySource13637.GetType();
            if (__querySource13637Type == typeof(BEngine.ProjectSystem.Editor.ProjectAssemblyNode[]) || __querySource13637Type == typeof(System.Collections.Generic.List<BEngine.ProjectSystem.Editor.ProjectAssemblyNode>))
            {
                System.ReadOnlySpan<BEngine.ProjectSystem.Editor.ProjectAssemblyNode> __querySource13637Span = __querySource13637Type == typeof(BEngine.ProjectSystem.Editor.ProjectAssemblyNode[]) ? (BEngine.ProjectSystem.Editor.ProjectAssemblyNode[])(object)__querySource13637 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.ProjectSystem.Editor.ProjectAssemblyNode>)(object)__querySource13637);
                foreach (var __queryItem13637 in __querySource13637Span)
                {
                    if ((__queryItem13637.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult13637 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem13637 in __querySource13637)
                {
                    if ((__queryItem13637.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult13637 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult13637)
            throw new InvalidDataException($"Assembly definition name '{assemblyName}' conflicts with the predefined project assembly.");
    }

    private void ValidateReferences(
        IReadOnlyCollection<ProjectAssemblyNode> nodes,
        IReadOnlyList<ProjectAssemblyDefinition> allDefinitions,
        IReadOnlyList<ProjectAssemblyDefinition> activeDefinitions,
        IReadOnlySet<string> externalAssemblies,
        IReadOnlySet<string>? externalEditorAssemblies)
    {
        var nodesByName = BuildIndex16314(nodes, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> BuildIndex16314(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> indexSource16314, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer16314)
        {
            ArgumentNullException.ThrowIfNull(indexSource16314, "source");
            var knownCount = indexSource16314.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode>(indexComparer16314);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> values;
            if (knownCount && indexSource16314 is global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode[] array)
                values = array;
            else if (knownCount && indexSource16314 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode>(capacity, indexComparer16314);
                foreach (var item in indexSource16314)
                    result.Add(IndexKey16314(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode>(values.Length, indexComparer16314);
                foreach (var item in values)
                    result.Add(IndexKey16314(item), item);
                return result;
            }
        
            string IndexKey16314(global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode node) => node.Name;
        }
        var allByName = BuildIndex16412(allDefinitions, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition> BuildIndex16412(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition> indexSource16412, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer16412)
        {
            ArgumentNullException.ThrowIfNull(indexSource16412, "source");
            var knownCount = indexSource16412.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition>(indexComparer16412);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition> values;
            if (knownCount && indexSource16412 is global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition[] array)
                values = array;
            else if (knownCount && indexSource16412 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition>(capacity, indexComparer16412);
                foreach (var item in indexSource16412)
                    result.Add(IndexKey16412(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition>(values.Length, indexComparer16412);
                foreach (var item in values)
                    result.Add(IndexKey16412(item), item);
                return result;
            }
        
            string IndexKey16412(global::BEngine.ProjectSystem.Editor.ProjectAssemblyDefinition definition) => definition.Document.Name;
        }
        var activeNames = activeDefinitions.Select(definition => definition.Document.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        foreach (var reference in node.References)
        {
            if (nodesByName.TryGetValue(reference, out var target))
            {
                if (!node.EditorOnly && target.EditorOnly)
                    throw new InvalidDataException(
                        $"Runtime assembly '{node.Name}' cannot reference editor-only assembly '{target.Name}'.");
                continue;
            }
            if (externalAssemblies.Contains(reference))
            {
                if (!node.EditorOnly && (externalEditorAssemblies?.Contains(reference) == true ||
                    reference.Equals(typeof(BEngine.Editor.EditorServices).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Runtime assembly '{node.Name}' cannot reference external editor assembly '{reference}'.");
                continue;
            }
            if (allByName.TryGetValue(reference, out var unavailable))
            {
                if (!node.EditorOnly && IsEditorDefinition(unavailable))
                    throw new InvalidDataException(
                        $"Runtime assembly '{node.Name}' cannot reference editor-only assembly '{reference}'.");
                var reason = !activeNames.Contains(reference)
                    ? "is excluded by its platform or define constraints"
                    : "is not available in this compilation target";
                throw new InvalidDataException(
                    $"Missing assembly reference '{reference}' required by '{node.Name}': {reason}. " +
                    unavailable.Path);
            }
            throw new InvalidDataException(
                $"Missing assembly reference '{reference}' required by '{node.Name}'.");
        }
    }

    private static IReadOnlyList<ProjectAssemblyNode> TopologicalSort(
        IReadOnlyCollection<ProjectAssemblyNode> nodes)
    {
        var byName = BuildIndex18690(nodes, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> BuildIndex18690(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> indexSource18690, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer18690)
        {
            ArgumentNullException.ThrowIfNull(indexSource18690, "source");
            var knownCount = indexSource18690.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode>(indexComparer18690);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> values;
            if (knownCount && indexSource18690 is global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode[] array)
                values = array;
            else if (knownCount && indexSource18690 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode>(capacity, indexComparer18690);
                foreach (var item in indexSource18690)
                    result.Add(IndexKey18690(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode>(values.Length, indexComparer18690);
                foreach (var item in values)
                    result.Add(IndexKey18690(item), item);
                return result;
            }
        
            string IndexKey18690(global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode node) => node.Name;
        }
        var indegree = BuildIndex18787(nodes, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, int> BuildIndex18787(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> indexSource18787, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer18787)
        {
            ArgumentNullException.ThrowIfNull(indexSource18787, "source");
            var knownCount = indexSource18787.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, int>(indexComparer18787);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> values;
            if (knownCount && indexSource18787 is global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode[] array)
                values = array;
            else if (knownCount && indexSource18787 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, int>(capacity, indexComparer18787);
                foreach (var item in indexSource18787)
                    result.Add(IndexKey18787(item), IndexValue18787(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, int>(values.Length, indexComparer18787);
                foreach (var item in values)
                    result.Add(IndexKey18787(item), IndexValue18787(item));
                return result;
            }
        
            string IndexKey18787(global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode node) => node.Name;
            int IndexValue18787(global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode _) => 0;
        }
        var dependents = BuildIndex18894(nodes, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>> BuildIndex18894(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> indexSource18894, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer18894)
        {
            ArgumentNullException.ThrowIfNull(indexSource18894, "source");
            var knownCount = indexSource18894.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>>(indexComparer18894);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> values;
            if (knownCount && indexSource18894 is global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode[] array)
                values = array;
            else if (knownCount && indexSource18894 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>>(capacity, indexComparer18894);
                foreach (var item in indexSource18894)
                    result.Add(IndexKey18894(item), IndexValue18894(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<string>>(values.Length, indexComparer18894);
                foreach (var item in values)
                    result.Add(IndexKey18894(item), IndexValue18894(item));
                return result;
            }
        
            string IndexKey18894(global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode node) => node.Name;
            global::System.Collections.Generic.List<string> IndexValue18894(global::BEngine.ProjectSystem.Editor.ProjectAssemblyNode _) => new List<string>();
        }
        foreach (var node in nodes)
        foreach (var reference in node.References)
        {
            if (!byName.ContainsKey(reference)) continue;
            indegree[node.Name]++;
            dependents[reference].Add(node.Name);
        }

        var ready = new SortedSet<string>(
            indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            Comparer<string>.Create((left, right) =>
            {
                var phase = byName[left].EditorOnly.CompareTo(byName[right].EditorOnly);
                return phase != 0 ? phase : StringComparer.OrdinalIgnoreCase.Compare(left, right);
            }));
        var ordered = new List<ProjectAssemblyNode>(nodes.Count);
        while (ready.Count > 0)
        {
            var name = ready.Min!;
            ready.Remove(name);
            ordered.Add(byName[name]);
            foreach (var dependent in dependents[name].OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                if (--indegree[dependent] == 0) ready.Add(dependent);
        }

        if (ordered.Count != nodes.Count)
        {
            var cycle = indegree.Where(pair => pair.Value > 0).Select(pair => pair.Key)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            throw new InvalidDataException(
                $"Assembly definition dependency cycle: {string.Join(" -> ", cycle)}.");
        }
        return ordered;
    }

    private static bool MatchesDefineConstraints(
        ProjectAssemblyDefinition definition,
        IReadOnlySet<string> symbols)
    {
        foreach (var value in definition.Document.DefineConstraints ?? [])
        {
            var constraint = value?.Trim();
            if (string.IsNullOrWhiteSpace(constraint))
                throw new InvalidDataException(
                    $"Assembly definition '{definition.Document.Name}' has an empty define constraint: " +
                    definition.Path);
            var negated = constraint.StartsWith('!');
            var symbol = negated ? constraint[1..].Trim() : constraint;
            ValidateDefineSymbol(symbol, definition.Path);
            if (negated == symbols.Contains(symbol)) return false;
        }
        return true;
    }

    private static bool MatchesPlatform(ProjectAssemblyDefinition definition, RuntimePlatform platform)
    {
        var included = definition.Document.IncludePlatforms ?? [];
        var excluded = definition.Document.ExcludePlatforms ?? [];
        foreach (var value in included.Concat(excluded)) ValidatePlatform(value, definition.Path);
        [global::BEngine.SkipLoopGuard]
        bool Query35311(global::System.Collections.Generic.IEnumerable<string> querySource35311)
        {
            ArgumentNullException.ThrowIfNull(querySource35311, "source");
            bool Matches35311(string value) => PlatformMatches(value, platform);
            global::System.ReadOnlySpan<string> queryValues35311;
            if (querySource35311.GetType() == typeof(string[]))
                queryValues35311 = (string[])querySource35311;
            else if (querySource35311.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues35311 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource35311);
            else
            {
                foreach (var value in querySource35311)
                    if (Matches35311(value))
                        return true;
                return false;
            }
        
            foreach (var value in queryValues35311)
                if (Matches35311(value))
                    return true;
            return false;
        }
        
        [global::BEngine.SkipLoopGuard]
        bool Query35388(global::System.Collections.Generic.IEnumerable<string> querySource35388)
        {
            ArgumentNullException.ThrowIfNull(querySource35388, "source");
            bool Matches35388(string value) => PlatformMatches(value, platform);
            global::System.ReadOnlySpan<string> queryValues35388;
            if (querySource35388.GetType() == typeof(string[]))
                queryValues35388 = (string[])querySource35388;
            else if (querySource35388.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues35388 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource35388);
            else
            {
                foreach (var value in querySource35388)
                    if (Matches35388(value))
                        return true;
                return false;
            }
        
            foreach (var value in queryValues35388)
                if (Matches35388(value))
                    return true;
            return false;
        }
        
        return (included.Count == 0 || Query35311(included)) && !Query35388(excluded);
    }

    private static bool PlatformMatches(string value, RuntimePlatform platform)
    {
        var candidate = value.Trim();
        if (Enum.TryParse<RuntimePlatform>(candidate, ignoreCase: true, out var exact))
            return exact == platform;
        var name = platform.ToString();
        if (candidate.Equals("Editor", StringComparison.OrdinalIgnoreCase))
            return name.EndsWith("Editor", StringComparison.Ordinal);
        if (candidate.Equals("Player", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("Standalone", StringComparison.OrdinalIgnoreCase))
            return name.EndsWith("Player", StringComparison.Ordinal);
        return name.StartsWith(candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidatePlatform(string? value, string path)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) ||
            (!Enum.TryParse<RuntimePlatform>(candidate, true, out _) &&
             !candidate.Equals("Editor", StringComparison.OrdinalIgnoreCase) &&
             !candidate.Equals("Player", StringComparison.OrdinalIgnoreCase) &&
             !candidate.Equals("Standalone", StringComparison.OrdinalIgnoreCase) &&
             !candidate.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
             !candidate.Equals("Linux", StringComparison.OrdinalIgnoreCase) &&
             !candidate.Equals("OSX", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Assembly definition '{path}' has unsupported platform '{value}'.");
    }

    private static void ValidateAssemblyName(string name, string path)
    {
        var trimmed = name.Trim();
        if (!name.Equals(trimmed, StringComparison.Ordinal) ||
            trimmed.IndexOfAny(EditorHostServices.Paths.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(EditorHostServices.Paths.DirectorySeparatorChar) || trimmed.Contains(EditorHostServices.Paths.AltDirectorySeparatorChar))
            throw new InvalidDataException(
                $"Assembly definition '{path}' has invalid assembly name '{name}'.");
    }

    private static void ValidateDefineSymbol(string symbol, string path)
    {
        if (symbol.Length == 0 || !(char.IsLetter(symbol[0]) || symbol[0] == '_') || Matches24245())
            throw new InvalidDataException($"Assembly definition '{path}' has invalid define constraint '{symbol}'.");
        [global::BEngine.SkipLoopGuard]
        bool Matches24245()
        {
            var matchSource24245 = symbol.Skip(1);
            ArgumentNullException.ThrowIfNull(matchSource24245, "source");
            global::System.ReadOnlySpan<char> matchValues24245;
            if (matchSource24245.GetType() == typeof(char[]))
            {
                matchValues24245 = (char[])(object)matchSource24245;
            }
            else if (matchSource24245.GetType() == typeof(global::System.Collections.Generic.List<char>))
            {
                matchValues24245 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<char>)(object)matchSource24245);
            }
            else
            {
                foreach (var character in (global::System.Collections.Generic.IEnumerable<char>)matchSource24245)
                {
                    if (!(char.IsLetterOrDigit(character) || character == '_'))
                        return true;
                }
        
                return false;
            }
        
            foreach (var character in matchValues24245)
            {
                if (!(char.IsLetterOrDigit(character) || character == '_'))
                    return true;
            }
        
            return false;
        }
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var root = EditorHostServices.Paths.TrimEndingDirectorySeparator(EditorHostServices.Paths.GetFullPath(directory)) +
                   EditorHostServices.Paths.DirectorySeparatorChar;
        return EditorHostServices.Paths.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
