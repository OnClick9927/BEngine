using System.Reflection;

namespace BEngine;

public static class EngineVersion
{
    /// <summary>The engine release version, without the build's source revision suffix.</summary>
    public static string Current { get; } = typeof(EngineVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}
