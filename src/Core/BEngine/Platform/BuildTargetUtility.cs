using BEngine.Build;
namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IBuildTargetCatalog through dependency injection.</summary>
public sealed class BuildTargetUtility
{
    private readonly IBuildTargetCatalog _implementation;

    public BuildTargetUtility(IBuildTargetCatalog implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static BuildTargetUtility Current => RuntimeServiceAccess.GetRequiredService<BuildTargetUtility>();

    public IReadOnlyList<BuildTargetDescriptor> All => _implementation.All;

    public IReadOnlyList<BuildPlatformDescriptor> Platforms => _implementation.Platforms;

    public BuildTargetDescriptor Get(string targetId) =>
        _implementation.Get(targetId);

    public bool TryGet(string targetId, out BuildTargetDescriptor descriptor) =>
        _implementation.TryGet(targetId, out descriptor);

    public BuildPlatformDescriptor GetPlatform(string platformId) =>
        _implementation.GetPlatform(platformId);

    public string GetPlatformId(BuildTargetPlatform platform) =>
        _implementation.GetPlatformId(platform);

    public string NormalizePlatformId(string targetOrPlatformId) =>
        _implementation.NormalizePlatformId(targetOrPlatformId);

    public BuildTargetDescriptor Resolve(BuildTargetPlatform platform) =>
        _implementation.Resolve(platform);

    public BuildTargetDescriptor Resolve(BuildTargetPlatform platform, BuildArchitecture architecture) =>
        _implementation.Resolve(platform, architecture);

    public BuildTargetDescriptor InferCurrentDesktop() =>
        _implementation.InferCurrentDesktop();

    public BuildTargetDescriptor InferCurrentDesktop(PlatformUtility platformService) =>
        _implementation.InferCurrentDesktop(platformService);

    public string InferCurrentDesktopPlatformId() =>
        _implementation.InferCurrentDesktopPlatformId();
}
