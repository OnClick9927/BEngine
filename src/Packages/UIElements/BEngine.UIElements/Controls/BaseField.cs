using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public abstract class BaseField<T> : VisualElement
{
    private string _label;
    private T _value = default!;
    private bool _isReadOnly;

    protected BaseField(string label = "") => _label = label;

    public string label { get => _label; set => Set(ref _label, value ?? string.Empty); }
    public T value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            MarkDirty();
            valueChanged?.Invoke(value);
        }
    }
    public bool isReadOnly { get => _isReadOnly; set => Set(ref _isReadOnly, value); }
    public event Action<T>? valueChanged;

    public void SetValueWithoutNotify(T value)
    {
        if (EqualityComparer<T>.Default.Equals(_value, value)) return;
        _value = value;
        MarkDirty();
    }

    internal void ChangeValueFromView(T value)
    {
        if (EqualityComparer<T>.Default.Equals(_value, value)) return;
        _value = value;
        valueChanged?.Invoke(value);
    }
}
