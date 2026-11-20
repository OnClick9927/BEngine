using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

internal static class ProjectScriptBuildPipeline
{
    internal static IReadOnlyList<System.Reflection.Assembly> LoadOwned(
        ProjectWorkspace workspace, ProjectScriptBuildResult build, List<AssemblyScope> owner,
        CancellationToken cancellationToken)
    {
        var artifacts = build.Runtime.CompiledAssemblies.Concat(build.Editor.CompiledAssemblies)
            .Where(artifact => artifact.CompiledThisBuild).ToArray();
        foreach (var artifact in artifacts) CompilationPipeline.RaiseAssemblyCompilationStarted(artifact.AssemblyPath);
        try
        {
            var loaded = new List<System.Reflection.Assembly>();
            foreach (var graph in new[] { build.Runtime, build.Editor })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assemblies = ProjectScriptCompiler.LoadCompiledGraph(graph, owner).LoadedAssemblies;
                RuntimeTypeCache.RegisterAssemblies(assemblies);
                foreach (var assembly in assemblies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
                    loaded.Add(assembly);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            ProjectScriptCompiler.PublishCompiledAssemblies(workspace, build.Runtime.CompiledAssemblies, true);
            ProjectScriptCompiler.PublishCompiledAssemblies(workspace, build.Editor.CompiledAssemblies, false);
            foreach (var artifact in artifacts) CompilationPipeline.RaiseAssemblyCompilationFinished(artifact.AssemblyPath, []);
            return loaded;
        }
        catch (Exception failure)
        {
            foreach (var artifact in artifacts)
                CompilationPipeline.RaiseAssemblyCompilationFinished(artifact.AssemblyPath,
                    [new CompilerMessage(failure.Message, artifact.Node.DefinitionPath, 0, 0, CompilerMessageType.Error)]);
            throw;
        }
    }

    internal static ProjectScriptBuildResult Build(
        ProjectWorkspace workspace,
        IReadOnlyDictionary<string, string> runtimeReferences,
        IReadOnlyDictionary<string, string> editorReferences,
        CancellationToken cancellationToken) => Build(
        workspace, runtimeReferences, editorReferences, ProcessUtility.Current, cancellationToken);

    internal static ProjectScriptBuildResult Build(
        ProjectWorkspace workspace,
        IReadOnlyDictionary<string, string> runtimeReferences,
        IReadOnlyDictionary<string, string> editorReferences,
        ProcessUtility processRunner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(runtimeReferences);
        ArgumentNullException.ThrowIfNull(editorReferences);
        ArgumentNullException.ThrowIfNull(processRunner);
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeConfiguration = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: false);
        var assemblyDatabase = new ProjectAssemblyDatabase(workspace);
        var sources = assemblyDatabase.CaptureSources(cancellationToken);
        var runtimeGraph = assemblyDatabase.BuildGraph(
            runtimeConfiguration.Platform,
            runtimeConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            runtimeReferences.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            includeEditorAssemblies: false, sourceSnapshot: sources, cancellationToken: cancellationToken);
        var runtime = ProjectScriptCompiler.CompileGraph(
            workspace,
            runtimeGraph.RuntimeAssemblies,
            runtimeReferences,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            runtimeConfiguration,
            publishRuntimeManifest: true,
            raiseCompilationEvents: false,
            publishOutputs: false,
            cancellationToken: cancellationToken,
            processRunner: processRunner);

        cancellationToken.ThrowIfCancellationRequested();
        var editorConfiguration = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: true);
        var externalAssemblies = editorReferences.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalEditorAssemblies = editorReferences.Keys.Except(runtimeReferences.Keys, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var editorGraph = assemblyDatabase.BuildGraph(
            runtimeConfiguration.Platform,
            runtimeConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            editorConfiguration.Platform,
            editorConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            externalAssemblies,
            includeEditorAssemblies: true,
            externalEditorAssemblies: externalEditorAssemblies,
            sourceSnapshot: sources, cancellationToken: cancellationToken);
        var runtimePaths = BuildIndex3203(runtime.CompiledAssemblies, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex3203(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.Editor.CompiledProjectAssembly> indexSource3203, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer3203)
        {
            ArgumentNullException.ThrowIfNull(indexSource3203, "source");
            var knownCount = indexSource3203.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer3203);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.Editor.CompiledProjectAssembly> values;
            if (knownCount && indexSource3203 is global::BEngine.ProjectSystem.Editor.CompiledProjectAssembly[] array)
                values = array;
            else if (knownCount && indexSource3203 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.Editor.CompiledProjectAssembly> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer3203);
                foreach (var item in indexSource3203)
                    result.Add(IndexKey3203(item), IndexValue3203(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer3203);
                foreach (var item in values)
                    result.Add(IndexKey3203(item), IndexValue3203(item));
                return result;
            }
        
            string IndexKey3203(global::BEngine.ProjectSystem.Editor.CompiledProjectAssembly artifact) => artifact.Node.Name;
            string IndexValue3203(global::BEngine.ProjectSystem.Editor.CompiledProjectAssembly artifact) => artifact.AssemblyPath;
        }
        var editor = ProjectScriptCompiler.CompileGraph(
            workspace,
            editorGraph.EditorAssemblies,
            editorReferences,
            runtimePaths,
            editorConfiguration,
            publishRuntimeManifest: false,
            raiseCompilationEvents: false,
            publishOutputs: false,
            cancellationToken: cancellationToken,
            processRunner: processRunner);
        return new ProjectScriptBuildResult(runtime, editor);
    }

    internal static void Apply(ProjectWorkspace workspace, ProjectScriptBuildResult build)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(build);
        var artifacts = build.Runtime.CompiledAssemblies.Concat(build.Editor.CompiledAssemblies)
            .Where(artifact => artifact.CompiledThisBuild).ToArray();
        foreach (var artifact in artifacts)
            CompilationPipeline.RaiseAssemblyCompilationStarted(artifact.AssemblyPath);
        try
        {
            ProjectScriptCompiler.ReplaceLoadedGraphs(workspace, build.Runtime, build.Editor);
        }
        catch (Exception exception)
        {
            foreach (var artifact in artifacts)
                CompilationPipeline.RaiseAssemblyCompilationFinished(artifact.AssemblyPath,
                    [new CompilerMessage(exception.Message, artifact.Node.DefinitionPath, 0, 0,
                        CompilerMessageType.Error)]);
            throw;
        }
        foreach (var artifact in artifacts)
            CompilationPipeline.RaiseAssemblyCompilationFinished(artifact.AssemblyPath, []);
    }
}
