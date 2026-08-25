namespace BEngine;

public sealed class Shader : BAsset
{
    private static readonly Dictionary<string, Shader> Shaders =
        new(StringComparer.Ordinal);

    private readonly string _shaderName;

    public string shaderName
    {
        get { return _shaderName; }
    }

    internal Shader(string shaderName)
    {
        _shaderName = shaderName;
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
