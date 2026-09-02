using BEngine.Documents;
using NumericsMatrix3x2 = System.Numerics.Matrix3x2;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetMaterial.png")]
[CreateAssetMenu(fileName = "New Material", menuName = "Rendering/Material", order = 200)]
public sealed class Material : BAsset
{
    private static readonly Dictionary<string, Material> BuiltInMaterials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _properties = new(StringComparer.Ordinal);
    private readonly HashSet<string> _keywords = new(StringComparer.Ordinal);

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
    public int passCount => 1;
    public string[] shaderKeywords
    {
        get => [.. _keywords.Order(StringComparer.Ordinal)];
        set
        {
            _keywords.Clear();
            foreach (var keyword in value ?? []) EnableKeyword(keyword);
        }
    }

    public Material() : this(Shader.Find("BEngine/Sprite")) { }

    public Material(Shader shader)
    {
        this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
        name = $"{shader.name} Material";
    }

    public Material(Material source) : this(source?.shader ?? throw new ArgumentNullException(nameof(source))) =>
        CopyPropertiesFromMaterial(source);

    internal static Material GetBuiltIn(string shaderName, string materialName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        var key = $"{shaderName}\n{materialName}";
        if (!BuiltInMaterials.TryGetValue(key, out var material))
            BuiltInMaterials[key] = material = new Material(Shader.Find(shaderName)) { name = materialName };
        return material;
    }

    public bool HasProperty(string propertyName)
    {
        ValidatePropertyName(propertyName);
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

    public void SetTexture(string propertyName, Texture? value)
    {
        ValidatePropertyName(propertyName);
        _properties[propertyName] = value;
    }

    public Texture? GetTexture(string propertyName)
    {
        ValidatePropertyName(propertyName);
        return _properties.TryGetValue(propertyName, out var value) ? value as Texture : null;
    }

    public void SetMatrix(string propertyName, NumericsMatrix3x2 value)
    {
        SetPropertyUnchecked(propertyName, value);
    }

    public NumericsMatrix3x2 GetMatrix(string propertyName)
    {
        ValidatePropertyName(propertyName);
        return _properties.TryGetValue(propertyName, out var value) && value is NumericsMatrix3x2 matrix
            ? matrix
            : NumericsMatrix3x2.Identity;
    }

    public void EnableKeyword(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        _keywords.Add(keyword.Trim());
    }

    public void DisableKeyword(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        _keywords.Remove(keyword.Trim());
    }

    public bool IsKeywordEnabled(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        return _keywords.Contains(keyword.Trim());
    }

    public bool SetPass(int pass)
    {
        if ((uint)pass >= (uint)passCount) throw new ArgumentOutOfRangeException(nameof(pass));
        return !string.IsNullOrWhiteSpace(shader.sourceCode) || !string.IsNullOrWhiteSpace(shader.shaderName);
    }

    public void CopyPropertiesFromMaterial(Material source)
    {
        ArgumentNullException.ThrowIfNull(source);
        shader = source.shader;
        renderQueue = source.renderQueue;
        _properties.Clear();
        foreach (var property in source._properties) _properties[property.Key] = property.Value;
        _keywords.Clear();
        _keywords.UnionWith(source._keywords);
    }

    internal void SetPropertyUnchecked(string propertyName, object value)
    {
        ValidatePropertyName(propertyName);
        _properties[propertyName] = value;
    }

    internal Color GetColorUnchecked(string propertyName, Color defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Color colorValue
            ? colorValue : defaultValue;

    internal Fix64 GetFloatUnchecked(string propertyName, Fix64 defaultValue = default) =>
        _properties.TryGetValue(propertyName, out var value) && value is Fix64 number ? number : defaultValue;

    public static Material Load(string path) => Document<Material>
        .Read(path, static sourcePath => FromFile(YamlUtility.Load<MaterialFile>(sourcePath)))
        .ToAsset();

    public void Save(string path) => Document<Material>.FromAsset(this)
        .Write(path, static (material, destination) => YamlUtility.Save(material.ToFile(), destination));

    private static Material FromFile(MaterialFile document)
    {
        if (document.Format != "BEngine.Material" || document.Version is not (1 or 2))
            throw new InvalidDataException("Unsupported material asset.");
        var material = new Material(Shader.Find(document.Shader))
        {
            name = document.Name,
            renderQueue = document.RenderQueue
        };
        foreach (var property in document.Properties ?? [])
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
                "Matrix3x2" when property.Values.Count == 6 => new NumericsMatrix3x2(
                    (float)Fix64.FromRaw(property.Values[0]), (float)Fix64.FromRaw(property.Values[1]),
                    (float)Fix64.FromRaw(property.Values[2]), (float)Fix64.FromRaw(property.Values[3]),
                    (float)Fix64.FromRaw(property.Values[4]), (float)Fix64.FromRaw(property.Values[5])),
                "Texture" => LoadTextureReference(property.Value),
                _ => throw new InvalidDataException($"Unsupported material property '{property.Name}'.")
            };
        material._keywords.UnionWith((document.Keywords ?? []).Where(static keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(static keyword => keyword.Trim()));
        return material;
    }

    private MaterialFile ToFile()
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
                NumericsMatrix3x2 matrix => new MaterialPropertyFile
                {
                    Name = propertyName,
                    Type = "Matrix3x2",
                    Values = new Fix64[] { (Fix64)matrix.M11, (Fix64)matrix.M12, (Fix64)matrix.M21,
                        (Fix64)matrix.M22, (Fix64)matrix.M31, (Fix64)matrix.M32 }
                        .Select(static item => item.RawValue).ToList()
                },
                Texture texture => new MaterialPropertyFile
                {
                    Name = propertyName,
                    Type = "Texture",
                    Value = TextureReference(texture)
                },
                null => new MaterialPropertyFile { Name = propertyName, Type = "Texture" },
                _ => null
            };
            if (entry is not null) properties.Add(entry);
        }
        return new MaterialFile
        {
            Name = name,
            Shader = shader.shaderName,
            RenderQueue = renderQueue,
            Properties = properties,
            Keywords = [.. _keywords.Order(StringComparer.Ordinal)]
        };
    }

    private static Texture? LoadTextureReference(string reference) => string.IsNullOrWhiteSpace(reference)
        ? null
        : BAssetReferenceLoader.LoadObjectReference(reference, typeof(Texture)) as Texture;

    private static string TextureReference(Texture texture)
    {
        if (texture.parentAssetGuid.HasValue && texture.localIdentifier > 0)
            return $"guid:{texture.parentAssetGuid.Value:N}#subasset={texture.localIdentifier}";
        if (!string.IsNullOrWhiteSpace(texture.assetPath) &&
            !texture.assetPath.StartsWith("memory-texture:", StringComparison.Ordinal)) return texture.assetPath;
        throw new InvalidOperationException("A runtime-only Texture cannot be saved in a Material asset.");
    }

    private static void ValidatePropertyName(string propertyName) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

    private sealed class MaterialFile
    {
        public string Format { get; set; } = "BEngine.Material";
        public int Version { get; set; } = 2;
        public string Name { get; set; } = "Material";
        public string Shader { get; set; } = "BEngine/Sprite";
        public int RenderQueue { get; set; } = 3000;
        public List<MaterialPropertyFile> Properties { get; set; } = [];
        public List<string> Keywords { get; set; } = [];
    }

    private sealed class MaterialPropertyFile
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<long> Values { get; set; } = [];
        public string Value { get; set; } = string.Empty;
    }
}
