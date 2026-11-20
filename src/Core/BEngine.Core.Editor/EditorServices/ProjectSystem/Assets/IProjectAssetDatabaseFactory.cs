namespace BEngine.ProjectSystem.Editor;

public interface IProjectAssetDatabaseFactory
{
    IProjectAssetDatabase Create(ProjectWorkspace workspace);
}
