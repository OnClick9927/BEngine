namespace BEngine;

public sealed class Shader : BObject
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Shader> Shaders =
        new(StringComparer.Ordinal);

    public string shaderName { get; }

    private Shader(string shaderName)
    {
        this.shaderName = shaderName;
        name = shaderName;
    }

    public static Shader Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Shaders.GetOrAdd(name, static shaderName => new Shader(shaderName));
    }
}

public sealed class Material : BObject
{
    private readonly Dictionary<string, object> _properties = new(StringComparer.Ordinal);

    public Shader shader { get; set; }
    public Color color
    {
        get => GetColor("_Color", Color.white);
        set => SetColor("_Color", value);
    }

    public Material(Shader shader)
    {
        this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
        name = $"{shader.name} Material";
    }

    public bool HasProperty(string propertyName) => _properties.ContainsKey(propertyName);
    public void SetColor(string propertyName, Color value) => _properties[propertyName] = value;
    public Color GetColor(string propertyName, Color defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Color colorValue ? colorValue : defaultValue;
    public void SetFloat(string propertyName, Fix64 value) => _properties[propertyName] = value;
    public Fix64 GetFloat(string propertyName, Fix64 defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Fix64 number ? number : defaultValue;
    public void SetVector(string propertyName, Vector4 value) => _properties[propertyName] = value;
    public Vector4 GetVector(string propertyName) =>
        _properties.TryGetValue(propertyName, out var value) && value is Vector4 vector ? vector : Vector4.zero;
}
