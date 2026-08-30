namespace BEngine;

[EditorIcon("Icons/Assets/AssetMaterial.png")]
[CreateAssetMenu(fileName = "New Material", menuName = "Rendering/Material", order = 200)]
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
    public int renderQueue { get; set; } = 3000;

    public Material() : this(Shader.Find("BEngine/Sprite")) { }

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
        renderQueue = source.renderQueue;
        _properties.Clear();
        foreach (var property in source._properties) _properties[property.Key] = property.Value;
    }

    internal void SetPropertyUnchecked(string propertyName, object value) => _properties[propertyName] = value;

    internal Color GetColorUnchecked(string propertyName, Color defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Color colorValue
            ? colorValue : defaultValue;

    internal Fix64 GetFloatUnchecked(string propertyName, Fix64 defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Fix64 number ? number : defaultValue;

    public static Material Load(string path)
    {
        var document = YamlUtility.Load<MaterialFile>(path);
        if (document.Format != "BEngine.Material" || document.Version != 1)
            throw new InvalidDataException("Unsupported material asset.");
        var material = new Material(Shader.Find(document.Shader))
        {
            name = document.Name,
            renderQueue = document.RenderQueue
        };
        foreach (var property in document.Properties)
            material._properties[property.Name] = property.Type switch
            {
                "Color" when property.Values.Count == 4 => new Color(
                    Fix64.FromRaw(property.Values[0]), Fix64.FromRaw(property.Values[1]),
                    Fix64.FromRaw(property.Values[2]), Fix64.FromRaw(property.Values[3])),
                "Vector4" when property.Values.Count == 4 => new Vector4(
                    Fix64.FromRaw(property.Values[0]), Fix64.FromRaw(property.Values[1]),
                    Fix64.FromRaw(property.Values[2]), Fix64.FromRaw(property.Values[3])),
                "Fix64" when property.Values.Count == 1 => Fix64.FromRaw(property.Values[0]),
                "Int32" when property.Values.Count == 1 => checked((int)property.Values[0]),
                _ => throw new InvalidDataException($"Unsupported material property '{property.Name}'.")
            };
        return material;
    }

    public void Save(string path)
    {
        var properties = new List<MaterialPropertyFile>(_properties.Count);
        foreach (var (propertyName, value) in _properties)
        {
            var entry = value switch
            {
                Color colorValue => new MaterialPropertyFile
                {
                    Name = propertyName,
                    Type = "Color",
                    Values = [colorValue.r.RawValue, colorValue.g.RawValue, colorValue.b.RawValue,
                        colorValue.a.RawValue]
                },
                Vector4 vector => new MaterialPropertyFile
                {
                    Name = propertyName,
                    Type = "Vector4",
                    Values = [vector.x.RawValue, vector.y.RawValue, vector.z.RawValue, vector.w.RawValue]
                },
                Fix64 number => new MaterialPropertyFile
                    { Name = propertyName, Type = "Fix64", Values = [number.RawValue] },
                int number => new MaterialPropertyFile
                    { Name = propertyName, Type = "Int32", Values = [number] },
                _ => null
            };
            if (entry is not null) properties.Add(entry);
        }
        YamlUtility.Save(new MaterialFile
        {
            Name = name,
            Shader = shader.shaderName,
            RenderQueue = renderQueue,
            Properties = properties
        }, path);
    }

    private sealed class MaterialFile
    {
        public string Format { get; set; } = "BEngine.Material";
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "Material";
        public string Shader { get; set; } = "BEngine/Sprite";
        public int RenderQueue { get; set; } = 3000;
        public List<MaterialPropertyFile> Properties { get; set; } = [];
    }

    private sealed class MaterialPropertyFile
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<long> Values { get; set; } = [];
    }
}
