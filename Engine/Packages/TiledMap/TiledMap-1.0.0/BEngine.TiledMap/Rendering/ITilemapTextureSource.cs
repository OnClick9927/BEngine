namespace BEngine.TiledMap;

public interface ITilemapTextureSource
{
    bool TryRead(string source, out byte[] bytes);
    string GetName(string source);
}
