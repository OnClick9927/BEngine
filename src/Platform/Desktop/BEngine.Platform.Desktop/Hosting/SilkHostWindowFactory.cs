using BEngine.Hosting;
using BEngine.Rendering;
namespace BEngine.Platform;

public sealed class SilkHostWindowFactory(ISceneRendererFactory rendererFactory) : IHostWindowFactory
{
    public IHostWindow Create(HostWindowOptions options) => new SilkHostWindow(options, rendererFactory);
}

