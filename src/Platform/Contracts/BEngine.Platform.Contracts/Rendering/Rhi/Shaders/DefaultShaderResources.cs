namespace BEngine.Rendering.Rhi;

public static class DefaultShaderResources
{
    private static readonly Dictionary<string, string> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Load(string resourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        if (!Cache.TryGetValue(resourcePath, out var source))
        {
            source = BEngine.Resources.Load<string>(resourcePath) ?? throw new FileNotFoundException(
                $"Default shader resource '{resourcePath}' was not found. Ensure the owning package Resources folder " +
                "is registered and included in the exported engine or player.");
            Cache[resourcePath] = source;
        }
        return source;
    }

    public static void ClearCache() => Cache.Clear();
}
