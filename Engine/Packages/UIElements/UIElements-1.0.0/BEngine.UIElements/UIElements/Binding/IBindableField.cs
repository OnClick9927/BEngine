namespace BEngine.UIElements;

internal interface IBindableField
{
    Type ValueType { get; }
    object? BoxedValue { get; }
    event Action<object?>? BoxedValueChanged;
    void SetBoxedValueWithoutNotify(object? value);
}
