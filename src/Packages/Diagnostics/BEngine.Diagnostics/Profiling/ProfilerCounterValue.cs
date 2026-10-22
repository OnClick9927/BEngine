using System.Globalization;

namespace BEngine.Profiling;

public sealed class ProfilerCounterValue<T> where T : struct, IConvertible
{
    private T _value;
    public string category { get; }
    public string name { get; }
    public T value
    {
        get => _value;
        set
        {
            _value = value;
            Profiler.SetCounterValue(category, name, value.ToDouble(CultureInfo.InvariantCulture));
        }
    }

    public ProfilerCounterValue(string category, string name, T initialValue = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.category = category.Trim();
        this.name = name.Trim();
        _value = initialValue;
    }
}
