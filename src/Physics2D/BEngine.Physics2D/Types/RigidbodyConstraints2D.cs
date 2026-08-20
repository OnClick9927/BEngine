namespace BEngine.Physics2D;

[Flags]
public enum RigidbodyConstraints2D
{
    None = 0,
    FreezePositionX = 1,
    FreezePositionY = 2,
    FreezeRotation = 4,
    FreezePosition = FreezePositionX | FreezePositionY,
    FreezeAll = FreezePosition | FreezeRotation
}
