namespace BEngine.TiledMap;

internal static class TiledMapRuntimeServices
{
    internal static ITilemapTextureSource TextureSource =>
        RuntimeServiceAccess.GetService<ITilemapTextureSource>() ??
        FileTilemapTextureSource.Shared;
}
