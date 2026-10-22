namespace BEngine.ShaderGraph;

public sealed class ShaderTextureValue
{
    public string name { get; set; } = "Texture";
    public Texture? texture { get; set; }
}

/// <summary>A shared, serializable set of graph properties. Renderer overrides take precedence.</summary>
[CreateAssetMenu(fileName = "New Shader Graph Material", menuName = "Rendering/Shader Graph Material", order = 206)]
[EditorIcon("Icons/Assets/AssetMaterial.png")]
public sealed class ShaderGraphMaterial : ScriptableObject
{
    public ShaderGraphAsset? graph { get; set; }
    public List<ShaderParameterValue> parameters { get; set; } = [];
    public List<ShaderTextureValue> textures { get; set; } = [];

    public void SetParameter(string name, Color value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var item = parameters.FirstOrDefault(item => item.name == name);
        if (item is null) parameters.Add(new() { name = name, value = value });
        else item.value = value;
    }

    public void SetTexture(string name, Texture? texture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var item = textures.FirstOrDefault(item => item.name == name);
        if (item is null) textures.Add(new() { name = name, texture = texture });
        else item.texture = texture;
    }
}
