namespace BEngine.UIElements;

public sealed class PointerDownEvent : PointerEventBase
{
    public int button { get; init; }
    public int clickCount { get; init; }
    public bool shiftKey { get; init; }
    public bool ctrlKey { get; init; }
    public bool altKey { get; init; }
    public bool commandKey { get; init; }
}
