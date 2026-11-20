namespace BEngine;

[EditorIcon("Icons/Assets/AssetShader.png")]
public sealed class Shader : BAsset
{
    private static readonly Dictionary<string, Shader> Shaders =
        new(StringComparer.Ordinal);

    private string _shaderName;
    private string _sourceCode;

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

    protected override object CaptureClonePayload() => new[] { _shaderName, _sourceCode };

    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        if (payload is not string[] { Length: 2 } values)
            throw new InvalidDataException("Invalid Shader clone payload.");
        _shaderName = values[0];
        _sourceCode = values[1];
    }

    public static Shader Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Shaders.TryGetValue(name, out var shader) || shader.isDestroyed)
            Shaders[name] = shader = new Shader(name);
        return shader;
    }
}
