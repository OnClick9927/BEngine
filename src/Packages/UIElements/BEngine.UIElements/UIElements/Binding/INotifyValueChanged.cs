namespace BEngine.UIElements;

public interface INotifyValueChanged<T>
{
    T value { get; set; }
    event Action<T>? valueChanged;
    void SetValueWithoutNotify(T value);
}
