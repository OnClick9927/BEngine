namespace BEngine;

public sealed class Shader : BAsset
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Shader> Shaders =
        new(StringComparer.Ordinal);

    private readonly string _shaderName;

    public string shaderName
    {
        get { MainThreadGuard.Ensure(); return _shaderName; }
    }

    private Shader(string shaderName)
    {
        _shaderName = shaderName;
        name = shaderName;
    }

    public static Shader Find(string name)
    {
        MainThreadGuard.Ensure();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Shaders.GetOrAdd(name, static shaderName => new Shader(shaderName));
    }
}
