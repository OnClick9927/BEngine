using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;

namespace BEngine.Editor.Build;

public interface IEditorVirtualContentSessionFactory
{
    IEditorVirtualContentSession Create(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        CancellationToken cancellationToken = default);
}
