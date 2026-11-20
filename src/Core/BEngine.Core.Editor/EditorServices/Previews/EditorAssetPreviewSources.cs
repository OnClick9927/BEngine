using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

/// <summary>Source metadata and revision caches shared by editor preview providers, without UI or decoder dependencies.</summary>
public static class EditorAssetPreviewSources
{
    public const string PreviewRevisionQuery = "?bengine-preview=";
    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, CachedImage> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CachedText> TextCache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Subscription(Action<AssetPreviewCacheInvalidation> Handler, Assembly Owner);
    private static readonly List<Subscription> Subscriptions = [];
    private static Action<AssetPreviewCacheInvalidation>? _invalidated;
    private static WeakReference<IEncodedImageCodec>? _codec;
    private static bool _observedCodec;
    private static bool _hadCodec;

    /// <summary>Invalidates cached presentation resources. Retired extension callbacks are released automatically.</summary>
    public static event Action<AssetPreviewCacheInvalidation> Invalidated
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        add
        {
            if (value is null) return;
            var owner = Assembly.GetCallingAssembly();
            lock (CacheGate)
            {
                foreach (Action<AssetPreviewCacheInvalidation> handler in value.GetInvocationList())
                    Subscriptions.Add(new(handler, owner));
                RebuildSubscriptions();
            }
        }
        remove
        {
            if (value is null) return;
            var handlers = value.GetInvocationList();
            lock (CacheGate)
            {
                for (var start = Subscriptions.Count - handlers.Length; start >= 0; start--)
                {
                    var match = true;
                    for (var i = 0; i < handlers.Length; i++)
                        if (!Subscriptions[start + i].Handler.Equals(handlers[i])) { match = false; break; }
                    if (!match) continue;
                    Subscriptions.RemoveRange(start, handlers.Length);
                    RebuildSubscriptions();
                    break;
                }
            }
        }
    }

    private static void RebuildSubscriptions()
    {
        _invalidated = null;
        foreach (var subscription in Subscriptions) _invalidated += subscription.Handler;
    }
    private static void Notify(AssetPreviewCacheInvalidation change)
    {
        Action<AssetPreviewCacheInvalidation>? callback;
        lock (CacheGate) callback = _invalidated;
        callback?.Invoke(change);
    }
    private static void EnsureCodec()
    {
        var current = EncodedImages.Current;
        bool changed;
        lock (CacheGate)
        {
            IEncodedImageCodec? previous = null;
            _codec?.TryGetTarget(out previous);
            changed = _observedCodec && (_hadCodec != (current is not null) || !ReferenceEquals(previous, current));
            if (changed) ImageCache.Clear();
            if (!_observedCodec || changed)
                _codec = current is null ? null : new(current);
            _observedCodec = true;
            _hadCodec = current is not null;
        }
        if (changed) Notify(new(null));
    }
    public static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (CacheGate)
        {
            Subscriptions.RemoveAll(s => s.Owner == assembly || s.Handler.Method.DeclaringType?.Assembly == assembly ||
                s.Handler.Target?.GetType().Assembly == assembly);
            if (assembly == typeof(EditorAssetPreviewSources).Assembly)
            {
                Subscriptions.Clear(); ImageCache.Clear(); TextCache.Clear(); _codec = null; _observedCodec = false;
            }
            RebuildSubscriptions();
        }
    }
    public static void Invalidate(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return;
        var path = ResolvePath(assetPath);
        lock (CacheGate) { ImageCache.Remove(path); TextCache.Remove(path); }
        Notify(new(path));
    }
    public static void ClearTemporaryAssetPreviews()
    {
        lock (CacheGate) { ImageCache.Clear(); TextCache.Clear(); }
        Notify(new(null));
    }

    public static AssetPreviewImage? GetImage(string path)
    {
        EnsureCodec();
        path = ResolvePath(path);
        var stamp = FileStamp.Read(path);
        CachedImage previous = default;
        var hadPrevious = false;
        lock (CacheGate)
        {
            if (ImageCache.TryGetValue(path, out previous))
            {
                if (previous.Stamp == stamp) return previous.Image;
                hadPrevious = true;
            }
        }

        AssetPreviewImage? image = null;
        if (stamp.Exists && TryReadImageDimensions(path, out var width, out var height))
        {
            image = new AssetPreviewImage(
                $"{path}{PreviewRevisionQuery}{stamp.LastWriteTicks:x}-{stamp.Length:x}",
                width, height);
        }
        lock (CacheGate) ImageCache[path] = new CachedImage(stamp, image);
        if (image is null && hadPrevious && previous.Image is not null)
            Notify(new AssetPreviewCacheInvalidation(path));
        return image;
    }

    public static string GetTextPreview(TextAsset asset)
    {
        var path = SourcePath(asset);
        var stamp = FileStamp.Read(path);
        lock (CacheGate)
            if (TextCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                return cached.Text;

        var text = asset.text;
        if (stamp.Exists)
            try { text = EditorCapabilities.GetRequiredService<FileUtility>().ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              ArgumentException) { }
        lock (CacheGate) TextCache[path] = new CachedText(stamp, text);
        return text;
    }

    public static string FileInfo(DefaultAsset asset)
    {
        var path = SourcePath(asset);
        if (EditorCapabilities.GetRequiredService<FileUtility>().DirectoryExists(path))
        {
            try
            {
                var directoryCount = EditorCapabilities.GetRequiredService<FileUtility>().EnumerateDirectories(
                    path, "*", SearchOption.TopDirectoryOnly).Count();
                var fileCount = EditorCapabilities.GetRequiredService<FileUtility>().EnumerateFiles(
                    path, "*", SearchOption.TopDirectoryOnly).Count();
                return $"{directoryCount} folders, {fileCount} files";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        var type = string.IsNullOrWhiteSpace(asset.assetType) ? "Asset" : asset.assetType;
        return $"{type}{SafeFileSize(path)}";
    }

    public static string TextInfo(string text, string type)
    {
        var lines = string.IsNullOrEmpty(text) ? 0 : global::System.MemoryExtensions.Count((text ?? throw new global::System.ArgumentNullException("source")).AsSpan(), '\n') + 1;
        return $"{type} | {lines} lines | {EditorUtility.FormatBytes(System.Text.Encoding.UTF8.GetByteCount(text))}";
    }

    public static string SafeFileSize(string path)
    {
        try
        {
            return EditorCapabilities.GetRequiredService<FileUtility>().FileExists(path)
                ? $" | {EditorUtility.FormatBytes(EditorCapabilities.GetRequiredService<FileUtility>().GetFileLength(path))}"
                : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return string.Empty; }
    }

    public static string IconFor(DefaultAsset asset)
    {
        var path = SourcePath(asset);
        if (string.IsNullOrWhiteSpace(path)) return EditorBuiltinIcons.Assets.Default;
        try { return EditorAssetIcons.GetIconPath(path); }
        catch (ArgumentException) { return EditorBuiltinIcons.Assets.Default; }
    }

    public static string DisplayName(DefaultAsset asset) =>
        DisplayName(asset.name, asset.assetPath, SourcePath(asset));

    public static string DisplayName(string? preferredName, string? virtualPath, string? sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(preferredName)) return preferredName.Trim();
        if (!string.IsNullOrWhiteSpace(virtualPath))
        {
            var remaining = virtualPath.AsSpan();
            while (!remaining.IsEmpty)
            {
                var separator = remaining.LastIndexOfAny('/', '\\');
                var segment = remaining[(separator + 1)..].Trim();
                if (!segment.IsEmpty) return segment.ToString();
                if (separator < 0) break;
                remaining = remaining[..separator];
            }
        }
        if (string.IsNullOrWhiteSpace(sourcePath)) return "Unnamed";
        var paths = EditorCapabilities.GetRequiredService<PathUtility>();
        var trimmed = sourcePath.TrimEnd(paths.DirectorySeparatorChar, paths.AltDirectorySeparatorChar);
        return paths.GetFileName(trimmed) is { Length: > 0 } name ? name : "Unnamed";
    }

    public static string SourcePath(DefaultAsset asset) =>
        !string.IsNullOrWhiteSpace(asset.sourcePath) ? ResolvePath(asset.sourcePath) : ResolvePath(asset.assetPath);

    public static string ResolveReference(string reference, string contextPath, Type assetType)
    {
        if (reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
            BAsset.Load(reference, assetType) is { } texture)
        {
            var importedPath = string.IsNullOrWhiteSpace(texture.artifactPath)
                ? texture.sourcePath
                : texture.artifactPath;
            if (EditorCapabilities.GetRequiredService<FileUtility>().FileExists(importedPath)) return importedPath;
        }
        if (EditorCapabilities.GetRequiredService<PathUtility>().IsPathRooted(reference)) return ResolvePath(reference);
        try
        {
            var runtimeAssetPath = RuntimeAssetReferences.ResolvePath(reference);
            if (EditorCapabilities.GetRequiredService<FileUtility>().FileExists(runtimeAssetPath)) return runtimeAssetPath;
        }
        catch (ArgumentException) { }
        try
        {
            var assetPath = AssetDatabase.ResolveAssetPath(reference);
            if (EditorCapabilities.GetRequiredService<FileUtility>().FileExists(assetPath)) return assetPath;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or
                                          ArgumentException) { }

        if (!string.IsNullOrWhiteSpace(contextPath))
        {
            var context = ResolvePath(contextPath);
            var combined = EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(EditorCapabilities.GetRequiredService<PathUtility>().Combine(EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(context) ?? string.Empty,
                reference.Replace('/', EditorCapabilities.GetRequiredService<PathUtility>().DirectorySeparatorChar)));
            if (EditorCapabilities.GetRequiredService<FileUtility>().FileExists(combined)) return combined;
        }
        return ResolvePath(reference);
    }

    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (EditorCapabilities.GetRequiredService<PathUtility>().IsPathRooted(path)) return EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(path);
        try { return AssetDatabase.ResolveAssetPath(path); }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or
                                          ArgumentException)
        {
            return EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(path);
        }
    }

    private static bool TryReadImageDimensions(string path, out int width, out int height)
    {
        width = height = 0;
        try { return EncodedImages.TryGetInfo(
            EditorCapabilities.GetRequiredService<FileUtility>().ReadAllBytes(path), out width, out height); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    public readonly record struct FileStamp(bool Exists, long Length, long LastWriteTicks)
    {
        public static FileStamp Read(string path)
        {
            try
            {
                if (!EditorCapabilities.GetRequiredService<FileUtility>().FileExists(path)) return default;
                return new FileStamp(
                    true,
                    EditorCapabilities.GetRequiredService<FileUtility>().GetFileLength(path),
                    EditorCapabilities.GetRequiredService<FileUtility>().GetLastWriteTimeUtc(path).Ticks);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              ArgumentException) { return default; }
        }
    }

    private readonly record struct CachedImage(FileStamp Stamp, AssetPreviewImage? Image);
    private readonly record struct CachedText(FileStamp Stamp, string Text);
}
