namespace BEngine.UIElements;

public sealed class WheelEvent : UIEventBase
{
    public Vector2 position { get; init; }
    public Vector2 delta { get; init; }
}
