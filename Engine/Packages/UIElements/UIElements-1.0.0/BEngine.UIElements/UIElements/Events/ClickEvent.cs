namespace BEngine.UIElements;

public sealed class ClickEvent : UIEventBase
{
    public int clickCount { get; init; } = 1;
}
