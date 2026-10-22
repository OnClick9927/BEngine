using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

public sealed class EditorVirtualContentSessionFactory : IEditorVirtualContentSessionFactory
{
    private readonly EditorVirtualAssetBundleBuilder _builder;

    public EditorVirtualContentSessionFactory() : this(
        EditorServiceAccess.GetRequiredService<EditorVirtualAssetBundleBuilder>())
    {
    }

    public EditorVirtualContentSessionFactory(EditorVirtualAssetBundleBuilder builder) =>
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));

    public IEditorVirtualContentSession Create(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        CancellationToken cancellationToken = default) =>
        EditorVirtualContentBootstrapper.Create(
            workspace, assetDatabase, _builder, cancellationToken);
}
