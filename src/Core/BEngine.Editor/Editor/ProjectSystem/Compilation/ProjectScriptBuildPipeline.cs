using BEngine.Editor;

namespace BEngine.ProjectSystem.Editor;

internal static class ProjectScriptBuildPipeline
{
    internal static ProjectScriptBuildResult Build(
        ProjectWorkspace workspace,
        IReadOnlyDictionary<string, string> runtimeReferences,
        IReadOnlyDictionary<string, string> editorReferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(runtimeReferences);
        ArgumentNullException.ThrowIfNull(editorReferences);
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeConfiguration = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: false);
        var runtimeGraph = new ProjectAssemblyDatabase(workspace).BuildGraph(
            runtimeConfiguration.Platform,
            runtimeConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            runtimeReferences.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            includeEditorAssemblies: false);
        var runtime = ProjectScriptCompiler.CompileGraph(
            workspace,
            runtimeGraph.RuntimeAssemblies,
            runtimeReferences,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            runtimeConfiguration,
            publishRuntimeManifest: true,
            raiseCompilationEvents: false,
            publishOutputs: false,
            cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var editorConfiguration = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: true);
        var externalAssemblies = editorReferences.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var editorGraph = new ProjectAssemblyDatabase(workspace).BuildGraph(
            runtimeConfiguration.Platform,
            runtimeConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            editorConfiguration.Platform,
            editorConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            externalAssemblies,
            includeEditorAssemblies: true);
        var runtimePaths = runtime.CompiledAssemblies.ToDictionary(
            artifact => artifact.Node.Name,
            artifact => artifact.AssemblyPath,
            StringComparer.OrdinalIgnoreCase);
        var editor = ProjectScriptCompiler.CompileGraph(
            workspace,
            editorGraph.EditorAssemblies,
            editorReferences,
            runtimePaths,
            editorConfiguration,
            publishRuntimeManifest: false,
            raiseCompilationEvents: false,
            publishOutputs: false,
            cancellationToken: cancellationToken);
        return new ProjectScriptBuildResult(runtime, editor);
    }

    internal static void Apply(ProjectWorkspace workspace, ProjectScriptBuildResult build)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(build);
        var artifacts = build.Runtime.CompiledAssemblies.Concat(build.Editor.CompiledAssemblies).ToArray();
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
