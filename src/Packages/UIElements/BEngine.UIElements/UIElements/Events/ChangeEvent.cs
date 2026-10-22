namespace BEngine.UIElements;

public sealed class ChangeEvent<T> : UIEventBase
{
    public required T previousValue { get; init; }
    public required T newValue { get; init; }
}
