using System.Collections.Concurrent;

namespace BEngine.Rendering.Rhi;

public static class DefaultShaderResources
{
    private static readonly ConcurrentDictionary<string, string> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Load(string resourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        return Cache.GetOrAdd(resourcePath, static path =>
            BEngine.Resources.Load<string>(path) ?? throw new FileNotFoundException(
                $"Default shader resource '{path}' was not found. Ensure the owning package Resources folder " +
                "is registered and included in the exported engine or player."));
    }

    public static void ClearCache() => Cache.Clear();
}
