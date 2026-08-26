namespace BEngine;

[EditorIcon("Icons/Assets/AssetShader.png")]
public sealed class Shader : FileAsset
{
    private static readonly Dictionary<string, Shader> Shaders =
        new(StringComparer.Ordinal);

    private readonly string _shaderName;
    private readonly string _sourceCode;

    public string shaderName
    {
        get { return _shaderName; }
    }
    public string sourceCode => _sourceCode;

    internal Shader(string shaderName, string sourceCode = "")
    {
        _shaderName = shaderName;
        _sourceCode = sourceCode ?? string.Empty;
        name = shaderName;
    }

    public static Shader Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Shaders.TryGetValue(name, out var shader))
            Shaders[name] = shader = new Shader(name);
        return shader;
    }
}
