namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectScriptBuildResult
{
    public ProjectScriptBuildResult(
        ProjectAssemblyBuildResult runtime,
        ProjectAssemblyBuildResult editor)
    {
        Runtime = runtime;
        Editor = editor;
    }

    public ProjectAssemblyBuildResult Runtime { get; }
    public ProjectAssemblyBuildResult Editor { get; }
}
