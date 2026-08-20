namespace BEngine;

public sealed class Material : BAsset
{
    private readonly Dictionary<string, object> _properties = new(StringComparer.Ordinal);
    private Shader _shader;

    public Shader shader
    {
        get { MainThreadGuard.Ensure(); return _shader; }
        set
        {
            MainThreadGuard.Ensure();
            _shader = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public Color color
    {
        get { MainThreadGuard.Ensure(); return GetColorUnchecked("_Color", Color.white); }
        set { MainThreadGuard.Ensure(); SetPropertyUnchecked("_Color", value); }
    }

    public Material(Shader shader)
    {
        _shader = shader ?? throw new ArgumentNullException(nameof(shader));
        name = $"{shader.name} Material";
    }

    public Material(Material source) : this(source?.shader ?? throw new ArgumentNullException(nameof(source))) =>
        CopyPropertiesFromMaterial(source);

    public bool HasProperty(string propertyName)
    {
        MainThreadGuard.Ensure();
        return _properties.ContainsKey(propertyName);
    }

    public void SetColor(string propertyName, Color value)
    {
        MainThreadGuard.Ensure();
        SetPropertyUnchecked(propertyName, value);
    }

    public Color GetColor(string propertyName, Color defaultValue = default)
    {
        MainThreadGuard.Ensure();
        return GetColorUnchecked(propertyName, defaultValue);
    }

    public void SetFloat(string propertyName, Fix64 value)
    {
        MainThreadGuard.Ensure();
        SetPropertyUnchecked(propertyName, value);
    }

    public Fix64 GetFloat(string propertyName, Fix64 defaultValue = default)
    {
        MainThreadGuard.Ensure();
        return GetFloatUnchecked(propertyName, defaultValue);
    }

    public void SetVector(string propertyName, Vector4 value)
    {
        MainThreadGuard.Ensure();
        SetPropertyUnchecked(propertyName, value);
    }

    public Vector4 GetVector(string propertyName)
    {
        MainThreadGuard.Ensure();
        return _properties.TryGetValue(propertyName, out var value) && value is Vector4 vector
            ? vector : Vector4.zero;
    }

    public void SetInt(string propertyName, int value)
    {
        MainThreadGuard.Ensure();
        SetPropertyUnchecked(propertyName, value);
    }

    public int GetInt(string propertyName)
    {
        MainThreadGuard.Ensure();
        return _properties.TryGetValue(propertyName, out var value) && value is int number ? number : 0;
    }

    public void CopyPropertiesFromMaterial(Material source)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(source);
        _shader = source._shader;
        _properties.Clear();
        foreach (var property in source._properties) _properties[property.Key] = property.Value;
    }

    internal void SetPropertyUnchecked(string propertyName, object value) => _properties[propertyName] = value;

    internal Color GetColorUnchecked(string propertyName, Color defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Color colorValue
            ? colorValue : defaultValue;

    internal Fix64 GetFloatUnchecked(string propertyName, Fix64 defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Fix64 number ? number : defaultValue;
}
