namespace BEngine;
public static class TextureSpriteExtensions
{
    public static Sprite CreateSprite(this Texture texture, Vector2 pivot, long localIdentifier = 21300000) => Sprite.Create(texture,pivot,localIdentifier);
}
