namespace BEngine.Editor;

/// <summary>Strongly typed extension options. Publishers own their settings; Core only stores them.</summary>
public sealed class PlatformBuildOptions
{
    private readonly Dictionary<Type, object> _values = new();
    public PlatformBuildOptions Set<T>(T options) where T : class
    {
        ArgumentNullException.ThrowIfNull(options);
        _values[typeof(T)] = options;
        return this;
    }
    public T Get<T>() where T : class, new()
    {
        if (_values.TryGetValue(typeof(T), out var value)) return (T)value;
        var options = new T();
        _values.Add(typeof(T), options);
        return options;
    }
}
