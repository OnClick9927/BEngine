namespace BEngine.UIElements;

public sealed class KeyDownEvent : UIEventBase
{
    public string key { get; init; } = string.Empty;
    public bool shiftKey { get; init; }
    public bool ctrlKey { get; init; }
    public bool altKey { get; init; }
    public bool commandKey { get; init; }
}
