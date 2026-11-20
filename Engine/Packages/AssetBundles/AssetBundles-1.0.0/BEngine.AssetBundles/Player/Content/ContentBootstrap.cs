using BEngine.Content;
using BEngine.AssetBundles;

namespace BEngine.Player.Content;

public enum ContentEnvironmentKind
{
    EditorVirtual,
    PublishedRemote,
    Workspace,
    EditorReal
}

public sealed record ActivatedContentRelease(
    string ReleaseId,
    ContentEnvironmentKind Environment,
    IAssetBundleManager? AssetBundles,
    bool Updated);

public interface IContentBootstrapper
{
    BValueTask<ActivatedContentRelease> PrepareAsync(BCancellationToken cancellationToken = default);
}

public sealed class WorkspaceContentBootstrapper : IContentBootstrapper
{
    private readonly string _releaseId;

    public WorkspaceContentBootstrapper(string releaseId = "workspace") =>
        _releaseId = string.IsNullOrWhiteSpace(releaseId)
            ? throw new ArgumentException("A workspace release id is required.", nameof(releaseId))
            : releaseId;

    public BValueTask<ActivatedContentRelease> PrepareAsync(BCancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return BValueTask<ActivatedContentRelease>.FromResult(new ActivatedContentRelease(
            _releaseId, ContentEnvironmentKind.Workspace, null, false));
    }
}
