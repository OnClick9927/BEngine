namespace BEngine.UIElements;

public sealed class PointerMoveEvent : PointerEventBase
{
    public Vector2 delta { get; init; }
    public bool shiftKey { get; init; }
    public bool ctrlKey { get; init; }
    public bool altKey { get; init; }
    public bool commandKey { get; init; }
}
