#if !BENGINE_PLAYER_PORTABLE
using BEngine.Rendering;
namespace BEngine.Player;

public sealed class SilkPlayerWindowFactory(ISceneRendererFactory rendererFactory) : IPlayerWindowFactory
{
    public IPlayerWindow Create(PlayerWindowOptions options) => new SilkPlayerWindow(options, rendererFactory);
}

#endif
