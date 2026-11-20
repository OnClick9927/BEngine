using System.Reflection;

namespace BEngine;

public static class EngineVersion
{
    /// <summary>The engine release version, without the build's source revision suffix.</summary>
    public static string Current { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(EngineVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var separator = version.IndexOf('+');
        return separator < 0 ? version : version[..separator];
    }
}
