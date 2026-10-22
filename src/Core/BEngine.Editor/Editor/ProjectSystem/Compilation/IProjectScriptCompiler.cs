using System.Reflection;

namespace BEngine.ProjectSystem.Editor;

internal interface IProjectScriptCompiler
{
    Assembly? CompileRuntimeAndLoad(ProjectWorkspace workspace);
    Assembly? CompileEditorAndLoad(
        ProjectWorkspace workspace,
        Assembly? runtimeAssembly,
        string coreEditorAssemblyPath);
    void ReleaseLoadContexts();
    IReadOnlyDictionary<string, string> PreferredAssemblyPaths(string assemblyPath);
    Dictionary<string, string> PackageReferences(
        ProjectWorkspace workspace,
        BPackageManager packages,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths = null);
    RuntimePackageSet ResolveRuntimePackageReferences(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions);
    ProjectScriptBuildResult Build(
        ProjectWorkspace workspace,
        IReadOnlyDictionary<string, string> runtimeReferences,
        IReadOnlyDictionary<string, string> editorReferences,
        CancellationToken cancellationToken);
    void Apply(ProjectWorkspace workspace, ProjectScriptBuildResult build);
}
