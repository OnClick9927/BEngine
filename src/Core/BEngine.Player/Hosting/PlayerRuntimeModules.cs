using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine.Hosting;
using BEngine.Platform;
namespace BEngine.Player;
/// <summary>Loads the manifest emitted from runtime-module metadata by the distribution build.</summary>
public static class PlayerRuntimeModules
{
    private static bool _initialized;
    public static void Initialize()
    {
        if (_initialized) return;
        var path = ResolveManifestPath(AppContext.BaseDirectory,
            RuntimeServices.GetService<ApplicationPathUtility>()?.ProcessPath);
        if (path is null) return;
        var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0)
                throw new InvalidDataException("Invalid runtime host module identity.");
            var assembly = AssemblyDomains.Current.LoadDefault(new AssemblyName(name));
            HostAssemblyRegistry.Register(assembly);
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
        _initialized = true;
    }

    private static string? ResolveManifestPath(string baseDirectory, string? processPath)
    {
        const string fileName = "player.modules.json";
        var path = Path.Combine(baseDirectory, fileName);
        if (File.Exists(path)) return path;

        // A self-extracting single-file host uses its extraction directory as
        // AppContext.BaseDirectory; deployment manifests remain beside the exe.
        if (!string.IsNullOrEmpty(processPath) && Path.IsPathFullyQualified(processPath) &&
            Path.GetDirectoryName(processPath) is { Length: > 0 } processDirectory)
        {
            path = Path.Combine(processDirectory, fileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
