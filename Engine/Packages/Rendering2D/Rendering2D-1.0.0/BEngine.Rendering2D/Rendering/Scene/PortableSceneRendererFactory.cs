using BEngine.Rendering.Rhi;
using BEngine.Platform;

namespace BEngine.Rendering;

public sealed class PortableSceneRendererFactory : ISceneRendererFactory
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;

    public PortableSceneRendererFactory(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    public ISceneRenderer Create(IGraphicsDevice device, bool ownsDevice = false) =>
        new PortableSceneRenderer(
            device, ownsDevice, _fileSystem, _pathService, _platformService);
}
