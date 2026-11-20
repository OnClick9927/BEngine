using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;

namespace BEngine.Editor.Build;

public sealed class EditorVirtualContentSessionFactory : IEditorVirtualContentSessionFactory
{
    private readonly EditorVirtualAssetBundleBuilder _builder;

    public EditorVirtualContentSessionFactory() : this(
        EditorCapabilities.GetRequiredService<EditorVirtualAssetBundleBuilder>())
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
