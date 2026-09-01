using BEngine.Documents;
using BEngine.Serialization;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem.Editor;

public sealed class ProjectAssemblyDatabase
{
    private readonly ProjectWorkspace _workspace;

    public ProjectAssemblyDatabase(ProjectWorkspace workspace) =>
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public IReadOnlyList<ProjectAssemblyDefinition> Discover()
    {
        return Directory.EnumerateFiles(_workspace.AssetsPath, "*.asmdef.yaml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProjectAssemblyDefinition(path, Load(path)))
            .ToArray();
    }

    public ProjectAssemblyDefinition? FindForSource(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        return FindForSource(fullPath, Discover());
    }

    internal ProjectAssemblyGraph BuildGraph(
        RuntimePlatform platform,
        IReadOnlySet<string> defineSymbols,
        IReadOnlySet<string> externalAssemblies,
        bool includeEditorAssemblies) => BuildGraph(
        platform, defineSymbols, platform, defineSymbols, externalAssemblies, includeEditorAssemblies);

    internal ProjectAssemblyGraph BuildGraph(
        RuntimePlatform runtimePlatform,
        IReadOnlySet<string> runtimeDefineSymbols,
        RuntimePlatform editorPlatform,
        IReadOnlySet<string> editorDefineSymbols,
        IReadOnlySet<string> externalAssemblies,
        bool includeEditorAssemblies)
    {
        ArgumentNullException.ThrowIfNull(runtimeDefineSymbols);
        ArgumentNullException.ThrowIfNull(editorDefineSymbols);
        ArgumentNullException.ThrowIfNull(externalAssemblies);
        var definitions = Discover();
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
        var activePaths = activeDefinitions.Select(definition => definition.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourcesByDefinition = activeDefinitions.ToDictionary(
            definition => definition.Path,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var unownedRuntimeSources = new List<string>();
        var unownedEditorSources = new List<string>();

        foreach (var sourcePath in Directory.EnumerateFiles(_workspace.AssetsPath, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var owner = FindForSource(sourcePath, definitions);
            if (owner is not null)
            {
                if (activePaths.Contains(owner.Path)) sourcesByDefinition[owner.Path].Add(sourcePath);
                continue;
            }

            if (IsInsideDirectory(_workspace.EditorScriptsPath, sourcePath))
                unownedEditorSources.Add(sourcePath);
            else
                unownedRuntimeSources.Add(sourcePath);
        }

        var nodes = activeDefinitions.Select(definition => new ProjectAssemblyNode
            {
                Name = definition.Document.Name,
                RootNamespace = definition.Document.RootNamespace,
                DefinitionPath = definition.Path,
                SourcePaths = sourcesByDefinition[definition.Path],
                References = NormalizeReferences(definition),
                EditorOnly = IsEditorDefinition(definition),
                AutoReferenced = definition.Document.AutoReferenced,
                AllowUnsafeCode = definition.Document.AllowUnsafeCode,
                Definition = definition.Document
            }).ToList();

        AddFallbackAssemblies(nodes, unownedRuntimeSources, unownedEditorSources, includeEditorAssemblies);
        ValidateReferences(nodes, definitions, activeDefinitions, externalAssemblies);
        return new ProjectAssemblyGraph(TopologicalSort(nodes));
    }

    private bool IsEditorDefinition(ProjectAssemblyDefinition definition) =>
        definition.Document.EditorOnly || IsInsideDirectory(_workspace.EditorScriptsPath, definition.Path);

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

    private static ProjectAssemblyDefinition? FindForSource(
        string sourcePath,
        IEnumerable<ProjectAssemblyDefinition> definitions) => definitions
        .Where(definition => IsInsideDirectory(Path.GetDirectoryName(definition.Path)!, sourcePath))
        .OrderByDescending(definition => Path.GetDirectoryName(definition.Path)!.Length)
        .ThenBy(definition => definition.Path, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();

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
            references.Add(name);
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
        var references = nodes.Where(node => node.AutoReferenced)
            .Select(node => node.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        if (nodes.Any(node => node.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Assembly definition name '{assemblyName}' conflicts with the predefined project assembly.");
    }

    private void ValidateReferences(
        IReadOnlyCollection<ProjectAssemblyNode> nodes,
        IReadOnlyList<ProjectAssemblyDefinition> allDefinitions,
        IReadOnlyList<ProjectAssemblyDefinition> activeDefinitions,
        IReadOnlySet<string> externalAssemblies)
    {
        var nodesByName = nodes.ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
        var allByName = allDefinitions.ToDictionary(
            definition => definition.Document.Name, StringComparer.OrdinalIgnoreCase);
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
            if (externalAssemblies.Contains(reference)) continue;
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
        var byName = nodes.ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
        var indegree = nodes.ToDictionary(node => node.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var dependents = nodes.ToDictionary(
            node => node.Name,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        foreach (var reference in node.References)
        {
            if (!byName.ContainsKey(reference)) continue;
            indegree[node.Name]++;
            dependents[reference].Add(node.Name);
        }

        var ready = new SortedSet<string>(
            indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            StringComparer.OrdinalIgnoreCase);
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
        return (included.Count == 0 || included.Any(value => PlatformMatches(value, platform))) &&
               !excluded.Any(value => PlatformMatches(value, platform));
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
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidDataException(
                $"Assembly definition '{path}' has invalid assembly name '{name}'.");
    }

    private static void ValidateDefineSymbol(string symbol, string path)
    {
        if (symbol.Length == 0 || !(char.IsLetter(symbol[0]) || symbol[0] == '_') ||
            symbol.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            throw new InvalidDataException(
                $"Assembly definition '{path}' has invalid define constraint '{symbol}'.");
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) +
                   Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
