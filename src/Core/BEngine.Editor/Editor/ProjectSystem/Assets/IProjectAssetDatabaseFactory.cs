namespace BEngine.ProjectSystem.Editor;

public interface IProjectAssetDatabaseFactory
{
    AssetDatabase Create(ProjectWorkspace workspace);
}
