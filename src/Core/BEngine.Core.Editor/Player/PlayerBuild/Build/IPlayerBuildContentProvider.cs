namespace BEngine.Editor.Build;
public interface IPlayerBuildContentProvider
{
    /// <summary>Public types identifying modules required before content can be loaded.</summary>
    IReadOnlyList<Type> RuntimeHostAnchors => [];
    /// <summary>Requirements of the provider's content format; platforms choose how to apply them.</summary>
    ResourcePackagingRequirements PackagingRequirements => ResourcePackagingRequirements.Empty;
    /// <summary>Reads an existing Player's provider-owned metadata; null means this layout is not recognized.</summary>
    BEngine.Hosting.HostDeploymentManifest? ReadDeploymentManifest(string playerDirectory, string productName) => null;
    Task PrepareAsync(PlayerBuildContext context, CancellationToken cancellationToken);
    /// <summary>Packs provider-owned metadata and archives after the engine has prepared
    /// raw assets, startup images and the deployment manifest.</summary>
    Task FinalizeAsync(PlayerBuildContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    IPlayerBuildTargetProvider Adapt(IPlatformBuildTargetProvider provider, PlatformBuildServices services) =>
        new PlayerPlatformBuildTargetAdapter(provider, services.FileSystem, services.Paths);
    Task<PlayerContentUpdateResult> BuildContentUpdateAsync(PlayerBuildRequest request, string outputDirectory,
        IProgress<PlayerBuildProgress>? progress, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This content provider does not support publishing content updates.");
    void SetLatestContentVersion(string packageDirectory, string version, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This content provider does not support publishing content updates.");
}
