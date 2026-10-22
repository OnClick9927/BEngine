using System.Reflection;
using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectScriptCompilerService : IProjectScriptCompiler
{
    private readonly ProcessUtility _processRunner;
    private readonly IPackageSourceCompiler _packageSourceCompiler;

    public ProjectScriptCompilerService(
        ProcessUtility processRunner,
        IPackageSourceCompiler packageSourceCompiler)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _packageSourceCompiler = packageSourceCompiler ??
                                 throw new ArgumentNullException(nameof(packageSourceCompiler));
    }

    public Assembly? CompileRuntimeAndLoad(ProjectWorkspace workspace)
    {
        var packages = RuntimePackageLoader.LoadEnabled(
            workspace,
            BPackageCatalog.DiscoverCached(workspace).packages,
            applyRuntimeState: true,
            _packageSourceCompiler);
        return ProjectScriptCompiler.CompileAndLoad(workspace, packages, _processRunner);
    }

    public Assembly? CompileEditorAndLoad(
        ProjectWorkspace workspace,
        Assembly? runtimeAssembly,
        string coreEditorAssemblyPath) => EditorProjectScriptCompiler.CompileAndLoad(
        workspace, runtimeAssembly, coreEditorAssemblyPath, _processRunner);

    public void ReleaseLoadContexts() => ProjectScriptCompiler.ReleaseLoadContexts();

    public IReadOnlyDictionary<string, string> PreferredAssemblyPaths(string assemblyPath) =>
        ProjectScriptCompiler.PreferredAssemblyPaths(assemblyPath);

    public Dictionary<string, string> PackageReferences(
        ProjectWorkspace workspace,
        BPackageManager packages,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths = null) =>
        EditorProjectScriptCompiler.PackageReferences(workspace, packages, preferredAssemblyPaths);

    public RuntimePackageSet ResolveRuntimePackageReferences(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions) =>
        RuntimePackageLoader.ResolveEnabledReferences(workspace, definitions);

    public ProjectScriptBuildResult Build(
        ProjectWorkspace workspace,
        IReadOnlyDictionary<string, string> runtimeReferences,
        IReadOnlyDictionary<string, string> editorReferences,
        CancellationToken cancellationToken) => ProjectScriptBuildPipeline.Build(
        workspace, runtimeReferences, editorReferences, _processRunner, cancellationToken);

    public void Apply(ProjectWorkspace workspace, ProjectScriptBuildResult build) =>
        ProjectScriptBuildPipeline.Apply(workspace, build);
}
