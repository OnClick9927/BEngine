using System.Reflection;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;
using BEngine.Serialization;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

public static class RuntimePackageLoader
{
    public static RuntimePackageSet LoadEnabled(ProjectWorkspace workspace)
        => LoadEnabled(workspace, BPackageCatalog.DiscoverCached(workspace).packages, true);

    internal static RuntimePackageSet LoadEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> packageDefinitions,
        bool applyRuntimeState = true) => LoadEnabled(
        workspace,
        packageDefinitions,
        applyRuntimeState,
        new PackageSourceCompilerService(BEngine.Platform.ProcessUtility.Current));

    internal static RuntimePackageSet LoadEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> packageDefinitions,
        bool applyRuntimeState,
        IPackageSourceCompiler packageSourceCompiler)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packageDefinitions);
        ArgumentNullException.ThrowIfNull(packageSourceCompiler);
        var definitions = BuildIndex1220(packageDefinitions.GroupBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex1220(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>> indexSource1220, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer1220)
        {
            ArgumentNullException.ThrowIfNull(indexSource1220, "source");
            var knownCount = indexSource1220.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer1220);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>> values;
            if (knownCount && indexSource1220 is global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>[] array)
                values = array;
            else if (knownCount && indexSource1220 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer1220);
                foreach (var item in indexSource1220)
                    result.Add(IndexKey1220(item), IndexValue1220(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer1220);
                foreach (var item in values)
                    result.Add(IndexKey1220(item), IndexValue1220(item));
                return result;
            }
        
            string IndexKey1220(global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition> group) => group.Key;
            global::BEngine.ProjectSystem.BPackageDefinition IndexValue1220(global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition> group) => group.First();
        }
        var manifest = EditorHostServices.FileSystem.FileExists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath)
            : new PackageManifestDocument();
        var manifestById = BuildIndex1679(manifest.Packages, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Documents.PackageReferenceDocument> BuildIndex1679(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.Documents.PackageReferenceDocument> indexSource1679, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer1679)
        {
            ArgumentNullException.ThrowIfNull(indexSource1679, "source");
            var knownCount = indexSource1679.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Documents.PackageReferenceDocument>(indexComparer1679);
            global::System.ReadOnlySpan<global::BEngine.Editor.Documents.PackageReferenceDocument> values;
            if (knownCount && indexSource1679 is global::BEngine.Editor.Documents.PackageReferenceDocument[] array)
                values = array;
            else if (knownCount && indexSource1679 is global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Documents.PackageReferenceDocument>(capacity, indexComparer1679);
                foreach (var item in indexSource1679)
                    result.Add(IndexKey1679(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.Documents.PackageReferenceDocument>(values.Length, indexComparer1679);
                foreach (var item in values)
                    result.Add(IndexKey1679(item), item);
                return result;
            }
        
            string IndexKey1679(global::BEngine.Editor.Documents.PackageReferenceDocument package) => package.Id;
        }
        var enabledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEngine.Core"] = typeof(BEngine.DependencyInjection.IEngineServiceModule).Assembly.Location,
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] =
                typeof(IServiceCollection).Assembly.Location
        };
        CoreModuleReferences.AddRuntime(references);

        if (applyRuntimeState)
            foreach (var package in manifest.Packages)
                RuntimePackageState.SetEnabled(package.Id, package.Enabled);

        var enabledDefinitions = manifest.Packages.Where(package => package.Enabled && package.Type != "project")
            .Select(package => definitions.TryGetValue(package.Id, out var definition)
                ? definition
                : throw new FileNotFoundException(
                    $"Enabled package '{package.Id}' is missing from " +
                    $"'{ProjectPackageCache.GetCacheRoot(workspace)}'."))
            .ToArray();
        var compilation = packageSourceCompiler.CompileEnabled(workspace, enabledDefinitions);

        foreach (var definition in enabledDefinitions)
        {
            enabledIds.Add(definition.Document.Id);
            if (definition.Document.Runtime is not { } runtime) continue;
            if (!compilation.TryGetAssemblyPath(runtime.Assembly, out var assemblyPath) ||
                !EditorHostServices.FileSystem.FileExists(assemblyPath))
                throw new FileNotFoundException(
                    $"Enabled package '{definition.Document.Id}' requires runtime assembly " +
                    $"'{runtime.Assembly}', but its source compilation produced no assembly.");
            references[runtime.Assembly] = assemblyPath;
            foreach (var hostReference in runtime.HostReferences)
                references[hostReference] = ResolveAssemblyPath(workspace, hostReference) ??
                    throw new FileNotFoundException($"Host reference '{hostReference}' is unavailable.");
        }

        BEngine.ProjectSystem.Editor.ProjectPackageSources.AddHostReferences(workspace, references, includeEditor: false);
        return new RuntimePackageSet(enabledIds, references);
    }

    internal static RuntimePackageSet ResolveEnabledReferences(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> packageDefinitions)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packageDefinitions);
        var definitions = BuildIndex4185(packageDefinitions.GroupBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex4185(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>> indexSource4185, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer4185)
        {
            ArgumentNullException.ThrowIfNull(indexSource4185, "source");
            var knownCount = indexSource4185.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer4185);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>> values;
            if (knownCount && indexSource4185 is global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>[] array)
                values = array;
            else if (knownCount && indexSource4185 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer4185);
                foreach (var item in indexSource4185)
                    result.Add(IndexKey4185(item), IndexValue4185(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer4185);
                foreach (var item in values)
                    result.Add(IndexKey4185(item), IndexValue4185(item));
                return result;
            }
        
            string IndexKey4185(global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition> group) => group.Key;
            global::BEngine.ProjectSystem.BPackageDefinition IndexValue4185(global::System.Linq.IGrouping<string, global::BEngine.ProjectSystem.BPackageDefinition> group) => group.First();
        }
        var manifest = EditorHostServices.FileSystem.FileExists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath)
            : new PackageManifestDocument();
        var enabledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEngine.Core"] = typeof(BEngine.DependencyInjection.IEngineServiceModule).Assembly.Location,
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] =
                typeof(IServiceCollection).Assembly.Location
        };
        CoreModuleReferences.AddRuntime(references);

        {
            var __filteredSource5011 = manifest.Packages;
            ArgumentNullException.ThrowIfNull(__filteredSource5011, "source");
            foreach (var package in __filteredSource5011)
            {
                if (!(package.Enabled && package.Type != "project"))
                    continue;
                if (!definitions.TryGetValue(package.Id, out var definition))
                    throw new FileNotFoundException($"Enabled package '{package.Id}' is missing from " + $"'{ProjectPackageCache.GetCacheRoot(workspace)}'.");
                enabledIds.Add(package.Id);
                if (definition.Document.Runtime is not { } runtime)
                    continue;
                var assemblyPath = ResolveAssemblyPath(workspace, runtime.Assembly);
                if (assemblyPath is null)
                    throw new FileNotFoundException($"Enabled package '{package.Id}' runtime assembly '{runtime.Assembly}' is not available. " + "Package compilation must complete before project scripts are queued.");
                references[runtime.Assembly] = assemblyPath;
                foreach (var hostReference in runtime.HostReferences)
                    references[hostReference] = ResolveAssemblyPath(workspace, hostReference) ?? throw new FileNotFoundException($"Host reference '{hostReference}' is unavailable.");
            }
        }

        BEngine.ProjectSystem.Editor.ProjectPackageSources.AddHostReferences(workspace, references, includeEditor: false);
        return new RuntimePackageSet(enabledIds, references);
    }

    internal static string? ResolveAssemblyPath(ProjectWorkspace workspace, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var loaded = Matches6632();
        [global::BEngine.SkipLoopGuard]
        global::System.Reflection.Assembly? Matches6632()
        {
            var matchSource6632 = EditorHostServices.Platform.GetLoadedAssemblies();
            ArgumentNullException.ThrowIfNull(matchSource6632, "source");
            global::System.ReadOnlySpan<global::System.Reflection.Assembly> matchValues6632;
            if (matchSource6632.GetType() == typeof(global::System.Reflection.Assembly[]))
                matchValues6632 = (global::System.Reflection.Assembly[])(object)matchSource6632;
            else if (matchSource6632.GetType() == typeof(global::System.Collections.Generic.List<global::System.Reflection.Assembly>))
                matchValues6632 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Reflection.Assembly>)(object)matchSource6632);
            else
            {
                foreach (var candidate in (global::System.Collections.Generic.IEnumerable<global::System.Reflection.Assembly>)matchSource6632)
                {
                    if (candidate.GetName().Name?.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) == true)
                        return candidate;
                }
        
                return default!;
            }
        
            foreach (var candidate in matchValues6632)
            {
                if (candidate.GetName().Name?.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) == true)
                    return candidate;
            }
        
            return default!;
        }
        if (ReadableLocation(loaded) is { } loadedPath) return loadedPath;

        if (BEngine.Editor.EditorInstanceContext.current is { } instance &&
            ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName, instance.scriptAssembliesPath) is
                { } instancePath)
            return instancePath;
        if (ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName) is { } projectPath)
            return projectPath;

        var applicationPath = EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, $"{assemblyName}.dll");
        return EditorHostServices.FileSystem.FileExists(applicationPath) ? EditorHostServices.Paths.GetFullPath(applicationPath) : null;
    }

    private static string? ReadableLocation(Assembly? assembly)
    {
        if (assembly is null || assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location)) return null;
        var path = EditorHostServices.Paths.GetFullPath(assembly.Location);
        return EditorHostServices.FileSystem.FileExists(path) ? path : null;
    }

}
