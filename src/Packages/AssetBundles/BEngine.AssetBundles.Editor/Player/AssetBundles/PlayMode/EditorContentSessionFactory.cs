using BEngine.AssetBundles;
using BEngine.Platform;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;

namespace BEngine.Editor.Build;

/// <summary>Selects the project's Editor content mode when each Play session is created.</summary>
public sealed class EditorContentSessionFactory : IEditorVirtualContentSessionFactory
{
    private readonly EditorVirtualAssetBundleBuilder _builder;
    private readonly FileUtility _files;
    private readonly PathUtility _paths;

    public EditorContentSessionFactory(EditorVirtualAssetBundleBuilder builder, FileUtility files, PathUtility paths)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public IEditorVirtualContentSession Create(ProjectWorkspace workspace, ProjectAssetDatabase assetDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetDatabase);
        cancellationToken.ThrowIfCancellationRequested();
        var settings = EditorPlayModeSettingsStore.Load(workspace.RootPath, _files, _paths);
        if (settings.Mode == EditorPlayModeContentMode.Virtual)
            return EditorVirtualContentBootstrapper.Create(workspace, assetDatabase, _builder, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.ReleaseDirectory))
            throw new InvalidOperationException("Real Editor Play Mode requires an AB Release Directory in Project Settings / Editor Play Mode.");
        var directory = _paths.GetFullPath(_paths.IsPathRooted(settings.ReleaseDirectory)
            ? settings.ReleaseDirectory : _paths.Combine(workspace.RootPath, settings.ReleaseDirectory));
        if (!_files.DirectoryExists(directory))
            throw new DirectoryNotFoundException($"Editor AssetBundle release directory does not exist: '{directory}'.");
        var manager = new AssetBundleManager(new AssetBundleRuntimeOptions
        {
            PackageName = settings.PackageName,
            BuiltInDirectory = directory,
            UsePersistentCache = false,
            EncryptionKey = workspace.GetContentEncryptionKey(),
            RequireEncryptedContent = workspace.GetContentEncryptionKey() is not null
        }, _files, _paths, BEngine.Networking.HttpNetworkRequestFactory.Shared);
        return new EditorRealContentSession(manager);
    }
}
