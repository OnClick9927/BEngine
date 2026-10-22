using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public abstract class BaseField<T> : VisualElement, INotifyValueChanged<T>, IBindableField
{
    private string _label;
    private T _value = default!;
    private bool _isReadOnly;

    protected BaseField(string label = "") => _label = label;

    public string label { get => _label; set { Set(ref _label, value ?? string.Empty); OnViewValueChanged(); } }
    public T value
    {
        get => _value;
        set
        {
            value = NormalizeValue(value);
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            var previous = _value;
            _value = value;
            OnViewValueChanged();
            MarkDirty();
            valueChanged?.Invoke(value);
            BoxedValueChanged?.Invoke(value);
            SendEvent(new ChangeEvent<T> { previousValue = previous, newValue = value });
        }
    }
    public bool isReadOnly { get => _isReadOnly; set { Set(ref _isReadOnly, value); OnViewValueChanged(); } }
    protected virtual void OnViewValueChanged() { }
    protected virtual T NormalizeValue(T value) => value;
    public event Action<T>? valueChanged;
    private event Action<object?>? BoxedValueChanged;

    Type IBindableField.ValueType => typeof(T);
    object? IBindableField.BoxedValue => _value;
    event Action<object?>? IBindableField.BoxedValueChanged
    {
        add => BoxedValueChanged += value;
        remove => BoxedValueChanged -= value;
    }

    public void SetValueWithoutNotify(T value)
    {
        value = NormalizeValue(value);
        if (EqualityComparer<T>.Default.Equals(_value, value)) return;
        _value = value;
        OnViewValueChanged();
        MarkDirty();
    }

    internal void ChangeValueFromView(T value)
    {
        value = NormalizeValue(value);
        if (EqualityComparer<T>.Default.Equals(_value, value)) return;
        var previous = _value;
        _value = value;
        OnViewValueChanged();
        MarkDirty();
        valueChanged?.Invoke(value);
        BoxedValueChanged?.Invoke(value);
        SendEvent(new ChangeEvent<T> { previousValue = previous, newValue = value });
    }

    void IBindableField.SetBoxedValueWithoutNotify(object? value) =>
        SetValueWithoutNotify(value is null ? default! : (T)value);
}
