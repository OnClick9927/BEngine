using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Platform;
namespace BEngine;
public interface ISpriteSourceProvider
{
    bool TryResolve(Sprite sprite, out SpriteRenderData2D data);
    Sprite? LoadReference(string reference, IReadOnlyDictionary<string, string>? context);
    void Invalidate();
}
public static class SpriteSourceRegistry
{
    private static readonly List<ISpriteSourceProvider> Providers = [];
    private static readonly ConditionalWeakTable<Sprite, SourceIdentity> SourceIdentities = new();
    private sealed class SourceIdentity
    {
        internal string Source = "", DataPath = "", WorkingDirectory = "", Key = "";
        internal PathUtility? Paths;
    }
    public static void Register(ISpriteSourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers.RemoveAll(item => item.GetType() == provider.GetType());
        Providers.Add(provider);
    }
    public static void UnregisterAssembly(Assembly assembly) => Providers.RemoveAll(p => p.GetType().Assembly == assembly);
    public static void Invalidate()
    {
        SourceIdentities.Clear();
        foreach (var provider in Providers.ToArray()) provider.Invalidate();
    }
    public static SpriteRenderData2D Resolve(Sprite? sprite)
    {
        if (sprite is null || sprite.isDestroyed) return SpriteRenderData2D.Solid;
        foreach (var provider in Providers) if (provider.TryResolve(sprite, out var data)) return data;
        if (string.IsNullOrWhiteSpace(sprite.Texture))
        {
            var missing = "missing:#" + sprite.name;
            return new SpriteRenderData2D(missing, missing, new Rect(0, 0, 1, 1), sprite.pivot, true);
        }
        var identity = ResolveIdentity(sprite);
        return new SpriteRenderData2D(sprite.Texture, identity, new Rect(0, 0, 1, 1), sprite.pivot, true)
        { PixelSize = sprite.texture is { } texture ? new Vector2(texture.width, texture.height) : Vector2.zero };
    }
    private static string ResolveIdentity(Sprite sprite)
    {
        var source = sprite.Texture;
        // Runtime texture identifiers are virtual addresses, not filesystem paths.
        if (source.StartsWith("memory-texture:", StringComparison.Ordinal)) return source;
        var paths = RuntimeServiceAccess.PathService;
        var dataPath = Application.dataPath;
        var end = dataPath.AsSpan().TrimEnd("/\\".AsSpan());
        var assetsRoot = end.Equals("Assets".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            end.EndsWith("/Assets".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            end.EndsWith("\\Assets".AsSpan(), StringComparison.OrdinalIgnoreCase);
        var projectRelative = HasPrefix(source, "Assets") || HasPrefix(source, "Packages");
        var workingDirectory = !paths.IsPathRooted(dataPath) || projectRelative && !assetsRoot
            ? RuntimeServiceAccess.PlatformService.CurrentDirectory : string.Empty;
        var cached = SourceIdentities.GetValue(sprite, static _ => new());
        if (cached.Source == source && cached.DataPath == dataPath && cached.WorkingDirectory == workingDirectory && ReferenceEquals(cached.Paths, paths)) return cached.Key;
        var identity = source;
        try { identity = AssetReferencePath.Key(source); } catch (ArgumentException) { }
        cached.Source = source; cached.DataPath = dataPath; cached.WorkingDirectory = workingDirectory; cached.Paths = paths; cached.Key = identity;
        return identity;
    }
    private static bool HasPrefix(string source, string prefix) => source.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        source.Length > prefix.Length && source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && source[prefix.Length] is '/' or '\\';
    public static Sprite? LoadSpriteReference(string reference, IReadOnlyDictionary<string, string>? context = null)
    {
        foreach (var provider in Providers.ToArray())
            if (provider.LoadReference(reference, context) is { } sprite) return sprite;
        try { return BAssetReferenceLoader.LoadObjectReference(reference, typeof(Sprite)) as Sprite; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or YamlDotNet.Core.YamlException) { return null; }
    }
}
