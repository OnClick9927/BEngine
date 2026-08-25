namespace BEngine;

public sealed class Material : BAsset
{
    private readonly Dictionary<string, object> _properties = new(StringComparer.Ordinal);

    public Shader shader
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Color color
    {
        get { return GetColorUnchecked("_Color", Color.white); }
        set { SetPropertyUnchecked("_Color", value); }
    }

    public Material(Shader shader)
    {
        this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
        name = $"{shader.name} Material";
    }

    public Material(Material source) : this(source?.shader ?? throw new ArgumentNullException(nameof(source))) =>
        CopyPropertiesFromMaterial(source);

    public bool HasProperty(string propertyName)
    {
        return _properties.ContainsKey(propertyName);
    }

    public void SetColor(string propertyName, Color value)
    {
        SetPropertyUnchecked(propertyName, value);
    }

    public Color GetColor(string propertyName, Color defaultValue = default)
    {
        return GetColorUnchecked(propertyName, defaultValue);
    }

    public void SetFloat(string propertyName, Fix64 value)
    {
        SetPropertyUnchecked(propertyName, value);
    }

    public Fix64 GetFloat(string propertyName, Fix64 defaultValue = default)
    {
        return GetFloatUnchecked(propertyName, defaultValue);
    }

    public void SetVector(string propertyName, Vector4 value)
    {
        SetPropertyUnchecked(propertyName, value);
    }

    public Vector4 GetVector(string propertyName)
    {
        return _properties.TryGetValue(propertyName, out var value) && value is Vector4 vector
            ? vector : Vector4.zero;
    }

    public void SetInt(string propertyName, int value)
    {
        SetPropertyUnchecked(propertyName, value);
    }

    public int GetInt(string propertyName)
    {
        return _properties.TryGetValue(propertyName, out var value) && value is int number ? number : 0;
    }

    public void CopyPropertiesFromMaterial(Material source)
    {
        ArgumentNullException.ThrowIfNull(source);
        shader = source.shader;
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
