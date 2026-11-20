using BEngine.Build;
namespace BEngine.Platform;
public interface IBuildTargetCatalog
{
    IReadOnlyList<BuildTargetDescriptor> All { get; }
    IReadOnlyList<BuildPlatformDescriptor> Platforms { get; }
    BuildTargetDescriptor Get(string targetId);
    bool TryGet(string targetId, out BuildTargetDescriptor descriptor);
    BuildPlatformDescriptor GetPlatform(string platformId);
    string GetPlatformId(BuildTargetPlatform platform);
    string NormalizePlatformId(string targetOrPlatformId);
    BuildTargetDescriptor Resolve(BuildTargetPlatform platform);
    BuildTargetDescriptor Resolve(BuildTargetPlatform platform, BuildArchitecture architecture);
    BuildTargetDescriptor InferCurrentDesktop();
    BuildTargetDescriptor InferCurrentDesktop(PlatformUtility platformService);
    string InferCurrentDesktopPlatformId();
}
