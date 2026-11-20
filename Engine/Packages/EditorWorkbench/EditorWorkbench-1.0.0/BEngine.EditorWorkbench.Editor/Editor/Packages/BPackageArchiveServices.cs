using BEngine.Platform;

namespace BEngine.Editor;

internal readonly record struct BPackageArchiveServices(
    FileUtility FileSystem,
    PathUtility PathService,
    PlatformUtility PlatformService,
    WaitUtility WaitService);
