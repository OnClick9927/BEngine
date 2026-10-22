namespace BEngine.Editor;

public interface IPlayerBuildPipeline
{
    IReadOnlyList<IPlayerBuildTargetProvider> RegisteredProviders { get; }
    void RegisterProvider(IPlayerBuildTargetProvider provider, bool replace = false);
    bool UnregisterProvider(string providerId);
    bool CanBuild(string targetId, out string reason);
    IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(string targetId);
    Task<PlayerBuildResult> BuildAsync(
        PlayerBuildRequest request,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<PlayerContentUpdateResult> BuildContentUpdateAsync(
        PlayerBuildRequest request,
        string outputDirectory,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
    void SetLatestContentVersion(
        string packageDirectory,
        string version,
        CancellationToken cancellationToken = default);
}
