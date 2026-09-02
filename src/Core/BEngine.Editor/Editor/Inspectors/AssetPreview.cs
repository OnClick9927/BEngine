using System.Buffers.Binary;
using RuntimeTextAsset = BEngine.TextAsset;

namespace BEngine.Editor;

/// <summary>Unity-style entry point for Inspector asset previews and mini thumbnails.</summary>
public static class AssetPreview
{
    internal const string PreviewRevisionQuery = "?bengine-preview=";
    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, CachedImage> ImageCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CachedText> TextCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CachedAtlas> AtlasCache =
        new(StringComparer.OrdinalIgnoreCase);
    internal static event Action<AssetPreviewCacheInvalidation>? CacheInvalidated;

    public static bool HasPreview(BObject? asset) => asset is BAsset or DefaultAsset;

    /// <summary>Returns a source-backed raster preview, or null for assets drawn procedurally.</summary>
    public static AssetPreviewImage? GetAssetPreview(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset switch
        {
            DefaultAsset defaultAsset when IsAtlas(defaultAsset) => GetAtlas(defaultAsset) is { Atlas: { } atlas }
                ? GetAtlasImage(atlas, SourcePath(defaultAsset))
                : null,
            DefaultAsset defaultAsset when IsTexture(defaultAsset) => GetImage(SourcePath(defaultAsset)),
            Texture texture => GetImage(texture.sourcePath),
            Sprite sprite => GetSpriteImage(sprite),
            TextureAtlas atlas => GetAtlasImage(atlas, AssetDatabase.GetAssetPath(atlas)),
            _ => null
        };
    }

    public static string GetMiniThumbnail(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (GetAssetPreview(asset) is { } preview) return preview.Source;
        return asset switch
        {
            DefaultAsset defaultAsset => IconFor(defaultAsset),
            PrefabAsset => EditorBuiltinIcons.Assets.Prefab,
            Scene => EditorBuiltinIcons.Assets.Scene,
            Material => EditorBuiltinIcons.Assets.Material,
            Shader => EditorBuiltinIcons.Assets.Shader,
            Texture => EditorBuiltinIcons.Assets.Image,
            Sprite => EditorBuiltinIcons.Assets.Image,
            Font => EditorBuiltinIcons.Assets.Font,
            Script => EditorBuiltinIcons.Assets.Script,
            RuntimeTextAsset => EditorBuiltinIcons.Assets.Text,
            TextureAtlas => EditorBuiltinIcons.Assets.Atlas,
            ScriptableObject scriptable => EditorIconRegistry.GetIconPath(scriptable.GetType()) ??
                                           EditorBuiltinIcons.Assets.Default,
            _ => IconForType(asset)
        };
    }

    public static bool IsLoadingAssetPreview(int instanceId) => false;

    public static void Invalidate(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return;
        var path = ResolvePath(assetPath);
        lock (CacheGate)
        {
            ImageCache.Remove(path);
            TextCache.Remove(path);
            AtlasCache.Remove(path);
        }
        CacheInvalidated?.Invoke(new AssetPreviewCacheInvalidation(path));
    }

    public static void ClearTemporaryAssetPreviews()
    {
        lock (CacheGate)
        {
            ImageCache.Clear();
            TextCache.Clear();
            AtlasCache.Clear();
        }
        CacheInvalidated?.Invoke(new AssetPreviewCacheInvalidation(null));
    }

    /// <summary>Draws either the raster or procedural preview supported for an asset.</summary>
    public static void DrawAssetPreview(BObject asset, Rect previewArea)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (previewArea.width <= 0 || previewArea.height <= 0) return;

        switch (asset)
        {
            case DefaultAsset defaultAsset:
                DrawDefaultAsset(defaultAsset, previewArea);
                break;
            case Material material:
                DrawMaterial(material, previewArea);
                break;
            case Texture texture:
                if (GetAssetPreview(texture) is { } image) DrawImage(previewArea, image);
                else DrawUnavailable(previewArea, EditorBuiltinIcons.Assets.Image, "Texture preview unavailable");
                break;
            case Sprite sprite:
                DrawSprite(sprite, previewArea);
                break;
            case Script script:
                DrawText(script.text, previewArea);
                break;
            case Font font:
                DrawIconSummary(previewArea, EditorBuiltinIcons.Assets.Font, font.name,
                    $"Font | Default size {font.defaultSize}");
                break;
            case TextureAtlas atlas:
                DrawAtlas(atlas, AssetDatabase.GetAssetPath(atlas), previewArea);
                break;
            case RuntimeTextAsset text:
                DrawText(text.text, previewArea);
                break;
            case Shader shader:
                DrawShader(shader, previewArea);
                break;
            case PrefabAsset prefab:
                DrawIconSummary(previewArea, EditorBuiltinIcons.Assets.Prefab, prefab.name,
                    $"{prefab.objectCount} objects  |  {prefab.componentCount} components");
                break;
            case Scene scene:
                DrawIconSummary(previewArea, EditorBuiltinIcons.Assets.Scene, scene.name,
                    $"{scene.gameObjects.Count} objects  |  {scene.rootCount} roots");
                break;
            case ScriptableObject scriptable:
                DrawIconSummary(previewArea,
                    EditorIconRegistry.GetIconPath(scriptable.GetType()) ?? EditorBuiltinIcons.Assets.Default,
                    scriptable.name,
                    ObjectNames.NicifyVariableName(scriptable.GetType().Name));
                break;
            default:
                DrawIconSummary(previewArea, IconForType(asset), asset.name,
                    ObjectNames.NicifyVariableName(asset.GetType().Name));
                break;
        }
    }

    public static string GetInfoString(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset switch
        {
            DefaultAsset defaultAsset when IsAtlas(defaultAsset) => AtlasInfo(defaultAsset),
            DefaultAsset defaultAsset when IsTexture(defaultAsset) => ImageInfo(defaultAsset),
            TextAsset text => TextInfo(GetTextPreview(text), text.assetType),
            AssemblyDefinitionAsset assembly =>
                $"Assembly Definition | {assembly.definition.References.Count} references",
            DefaultAsset defaultAsset => FileInfo(defaultAsset),
            Material material => $"Material | Shader: {material.shader.shaderName}",
            Texture texture => texture.width > 0 && texture.height > 0
                ? $"{texture.width} x {texture.height} | {texture.compressionFormat}"
                : $"Texture | {texture.compressionFormat}",
            Sprite sprite => TextureAtlasResolver.TryGetPackedAtlas(sprite, out var atlasPath, out _, out _)
                ? $"Sprite | Packed in {atlasPath} | Pivot {sprite.PivotX:0.###}, {sprite.PivotY:0.###}"
                : $"Sprite | {sprite.Texture} | Pivot {sprite.PivotX:0.###}, {sprite.PivotY:0.###}",
            Font font => $"Font | Default size {font.defaultSize} | {font.characterSet}",
            Script script => TextInfo(script.text, "Script"),
            TextureAtlas atlas => $"{atlas.Width} x {atlas.Height} | {atlas.Sprites.Count} sprites",
            RuntimeTextAsset text => TextInfo(text.text, "Text"),
            Shader shader => $"Shader | {shader.shaderName}",
            PrefabAsset prefab => $"{prefab.objectCount} GameObjects, {prefab.componentCount} Components",
            Scene scene => $"{scene.gameObjects.Count} GameObjects, {scene.rootCount} roots",
            ScriptableObject scriptable => ObjectNames.NicifyVariableName(scriptable.GetType().Name),
            _ => ObjectNames.NicifyVariableName(asset.GetType().Name)
        };
    }

    private static void DrawDefaultAsset(DefaultAsset asset, Rect area)
    {
        if (IsAtlas(asset))
        {
            if (GetAtlas(asset) is { Atlas: { } atlas })
            {
                DrawAtlas(atlas, asset.sourcePath, area);
                return;
            }
            DrawUnavailable(area, IconFor(asset), "Atlas preview unavailable");
            return;
        }

        if (IsTexture(asset))
        {
            if (GetAssetPreview(asset) is { } image)
            {
                DrawImage(area, image);
                return;
            }
            DrawUnavailable(area, IconFor(asset), "Image preview unavailable");
            return;
        }

        if (asset is TextAsset text)
        {
            var previewText = GetTextPreview(text);
            if (asset.assetType.Equals(nameof(Shader), StringComparison.OrdinalIgnoreCase))
                DrawShaderSource(previewText, area);
            else
                DrawText(previewText, area);
            return;
        }

        var sourcePath = SourcePath(asset);
        var detail = Directory.Exists(sourcePath)
            ? "Folder"
            : string.IsNullOrWhiteSpace(asset.assetType) ? "Asset" : asset.assetType;
        DrawIconSummary(area, IconFor(asset), DisplayName(asset), detail);
    }

    private static void DrawAtlas(TextureAtlas atlas, string contextPath, Rect area)
    {
        var image = GetAtlasImage(atlas, contextPath);
        if (image is null)
        {
            DrawUnavailable(area, EditorBuiltinIcons.Assets.Image, "Atlas texture unavailable");
            return;
        }

        var fitted = DrawImage(area, image.Value);
        if (atlas.Width <= 0 || atlas.Height <= 0) return;
        var selection = EditorStyles.selectionRect.normal.backgroundColor;
        var outline = new Color(selection.r, selection.g, selection.b, Fix64.FromDecimal(0.72m));
        foreach (var sprite in atlas.Sprites.Take(128))
        {
            var spriteRect = new Rect(
                fitted.x + fitted.width * sprite.X / atlas.Width,
                fitted.y + fitted.height * sprite.Y / atlas.Height,
                fitted.width * sprite.Width / atlas.Width,
                fitted.height * sprite.Height / atlas.Height);
            DrawBorder(spriteRect, outline);
        }
    }

    private static void DrawSprite(Sprite sprite, Rect area)
    {
        var image = GetSpriteImage(sprite);
        if (image is null)
        {
            DrawUnavailable(area, EditorBuiltinIcons.Assets.Image, "Sprite texture unavailable");
            return;
        }
        var fitted = DrawImage(area, image.Value);
        if (!TextureAtlasResolver.TryGetPackedAtlas(sprite, out _, out var atlas, out var region) ||
            atlas.Width <= 0 || atlas.Height <= 0) return;
        var regionRect = new Rect(
            fitted.x + fitted.width * region.X / atlas.Width,
            fitted.y + fitted.height * region.Y / atlas.Height,
            fitted.width * region.Width / atlas.Width,
            fitted.height * region.Height / atlas.Height);
        DrawBorder(regionRect, EditorStyles.selectionRect.normal.backgroundColor);
    }

    private static Rect DrawImage(Rect area, AssetPreviewImage image)
    {
        var inner = Inset(area, 5);
        var fitted = Fit(inner, image.Width, image.Height);
        DrawCheckerboard(fitted);
        GUI.DrawTexture(fitted, image.Source);
        DrawBorder(fitted, StyleBorder(EditorStyles.frameBox));
        return fitted;
    }

    private static void DrawMaterial(Material material, Rect area)
    {
        var inner = Inset(area, 12);
        var swatch = new Rect(inner.x + inner.width * Fix64.FromDecimal(0.12m),
            inner.y + inner.height * Fix64.FromDecimal(0.12m),
            inner.width * Fix64.FromDecimal(0.76m), inner.height * Fix64.FromDecimal(0.66m));
        DrawCheckerboard(swatch);
        GUI.DrawRect(swatch, material.color);
        DrawBorder(swatch, StyleBorder(EditorStyles.colorPickerBox));
        var label = new Rect(inner.x, swatch.yMax + 4, inner.width,
            Fix64.Max(0, inner.yMax - swatch.yMax - 4));
        GUI.Label(label, material.shader.shaderName, EditorStyles.centeredMiniLabel);
    }

    private static void DrawShader(Shader shader, Rect area)
    {
        var background = EditorStyles.viewBackground;
        var selection = EditorStyles.selectionRect;
        GUI.DrawGradientRect(area, background.normal.backgroundColor, selection.normal.backgroundColor,
            background.hover.backgroundColor, selection.active.backgroundColor);
        DrawCenteredIcon(area, EditorBuiltinIcons.Assets.Shader, shader.shaderName);
    }

    private static void DrawShaderSource(string text, Rect area)
    {
        var headerHeight = Fix64.Min(28, area.height / 4);
        var header = new Rect(area.x, area.y, area.width, headerHeight);
        var titlebar = EditorStyles.inspectorTitlebar;
        GUI.DrawGradientRect(header, titlebar.normal.backgroundColor, titlebar.active.backgroundColor,
            titlebar.hover.backgroundColor, titlebar.focused.backgroundColor);
        GUI.Label(Inset(header, 4), new GUIContent("Shader source", EditorBuiltinIcons.Assets.Shader),
            EditorStyles.boldLabel);
        DrawText(text, new Rect(area.x, header.yMax, area.width, Fix64.Max(0, area.yMax - header.yMax)));
    }

    private static void DrawText(string text, Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.textArea);
        var inner = Inset(area, 5);
        var lineHeight = Fix64.Max(16, EditorStyles.miniLabel.fontSize + 5);
        var visibleLines = Math.Max(1, (int)(inner.height / lineHeight));
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < Math.Min(lines.Length, visibleLines); index++)
        {
            var lineRect = new Rect(inner.x, inner.y + lineHeight * index, inner.width, lineHeight);
            if ((index & 1) != 0)
                GUI.DrawRect(lineRect, EditorStyles.scrollViewAlt.normal.backgroundColor);
            var line = Sanitize(lines[index]);
            GUI.Label(lineRect, $"{index + 1,3}  {line}", EditorStyles.miniLabel);
        }
    }

    private static void DrawIconSummary(Rect area, string icon, string title, string detail)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var iconSize = Fix64.Clamp(Fix64.Min(area.width, area.height) * Fix64.FromDecimal(0.46m), 36, 88);
        var iconRect = new Rect(area.x + (area.width - iconSize) * Fix64.Half,
            area.y + Fix64.Max(8, (area.height - iconSize - 42) * Fix64.Half), iconSize, iconSize);
        GUI.DrawTexture(iconRect, icon);
        var titleRect = new Rect(area.x + 6, iconRect.yMax + 4, Fix64.Max(0, area.width - 12), 20);
        GUI.Label(titleRect, string.IsNullOrWhiteSpace(title) ? "Unnamed asset" : title,
            EditorStyles.centeredBoldLabel);
        var detailRect = new Rect(area.x + 6, titleRect.yMax, Fix64.Max(0, area.width - 12), 18);
        GUI.Label(detailRect, detail, EditorStyles.centeredMiniLabel);
    }

    private static void DrawUnavailable(Rect area, string icon, string message) =>
        DrawIconSummary(area, icon, message, "The source file is missing or unsupported");

    private static void DrawCenteredIcon(Rect area, string icon, string label)
    {
        var size = Fix64.Clamp(Fix64.Min(area.width, area.height) * Fix64.FromDecimal(0.42m), 32, 76);
        var iconRect = new Rect(area.x + (area.width - size) * Fix64.Half,
            area.y + (area.height - size - 24) * Fix64.Half, size, size);
        GUI.DrawTexture(iconRect, icon);
        GUI.Label(new Rect(area.x + 6, iconRect.yMax + 4, Fix64.Max(0, area.width - 12), 20), label,
            EditorStyles.centeredBoldLabel);
    }

    private static void DrawCheckerboard(Rect area)
    {
        if (area.width <= 0 || area.height <= 0) return;
        var tile = Fix64.Max(8, Fix64.Max(area.width, area.height) / 24);
        var columns = Math.Max(1, (int)Math.Ceiling((double)(area.width / tile)));
        var rows = Math.Max(1, (int)Math.Ceiling((double)(area.height / tile)));
        var first = EditorStyles.textField.normal.backgroundColor;
        var second = EditorStyles.frameBox.normal.backgroundColor;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var x = area.x + tile * column;
            var y = area.y + tile * row;
            GUI.DrawRect(new Rect(x, y, Fix64.Min(tile, area.xMax - x), Fix64.Min(tile, area.yMax - y)),
                ((row + column) & 1) == 0 ? first : second);
        }
    }

    private static Color StyleBorder(GUIStyle style)
    {
        var color = style.normal.borderColor;
        return color.a > 0 ? color : EditorStyles.separator.normal.backgroundColor;
    }

    private static AtlasPreviewData? GetAtlas(DefaultAsset asset)
    {
        var path = SourcePath(asset);
        if (!File.Exists(path)) return null;
        var stamp = FileStamp.Read(path);
        lock (CacheGate)
        {
            if (AtlasCache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Data;
        }

        AtlasPreviewData? data;
        try
        {
            var atlas = TextureAtlas.Load(path);
            data = new AtlasPreviewData(atlas);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or ArgumentException)
        {
            data = null;
        }
        lock (CacheGate) AtlasCache[path] = new CachedAtlas(stamp, data);
        return data;
    }

    private static AssetPreviewImage? GetAtlasImage(TextureAtlas atlas, string contextPath)
    {
        if (string.IsNullOrWhiteSpace(atlas.Texture)) return null;
        return GetImage(ResolveReference(atlas.Texture, contextPath));
    }

    private static AssetPreviewImage? GetSpriteImage(Sprite sprite)
    {
        if (TextureAtlasResolver.TryGetPackedAtlas(sprite, out var atlasPath, out var atlas, out _))
            return GetAtlasImage(atlas, ResolvePath(atlasPath));
        if (string.IsNullOrWhiteSpace(sprite.Texture)) return null;
        return GetImage(ResolveReference(sprite.Texture, sprite.sourcePath));
    }

    private static AssetPreviewImage? GetImage(string path)
    {
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
            CacheInvalidated?.Invoke(new AssetPreviewCacheInvalidation(path));
        return image;
    }

    private static string GetTextPreview(TextAsset asset)
    {
        var path = SourcePath(asset);
        var stamp = FileStamp.Read(path);
        lock (CacheGate)
            if (TextCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                return cached.Text;

        var text = asset.text;
        if (stamp.Exists)
            try { text = File.ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              ArgumentException) { }
        lock (CacheGate) TextCache[path] = new CachedText(stamp, text);
        return text;
    }

    private static string AtlasInfo(DefaultAsset asset)
    {
        var data = GetAtlas(asset)?.Atlas;
        return data is null ? "Texture Atlas | Preview unavailable" :
            $"{data.Width} x {data.Height} | {data.Sprites.Count} sprites";
    }

    private static string ImageInfo(DefaultAsset asset)
    {
        var image = GetAssetPreview(asset);
        var suffix = SafeFileSize(SourcePath(asset));
        var format = Path.GetExtension(SourcePath(asset)).TrimStart('.').ToUpperInvariant();
        return image is null ? $"{asset.assetType} | Preview unavailable{suffix}" :
            $"{image.Value.Width} x {image.Value.Height} | {format}{suffix}";
    }

    private static string FileInfo(DefaultAsset asset)
    {
        var path = SourcePath(asset);
        if (Directory.Exists(path))
        {
            try
            {
                var entries = new DirectoryInfo(path).EnumerateFileSystemInfos().ToArray();
                return $"{entries.Count(entry => entry is DirectoryInfo)} folders, " +
                       $"{entries.Count(entry => entry is FileInfo)} files";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        var type = string.IsNullOrWhiteSpace(asset.assetType) ? "Asset" : asset.assetType;
        return $"{type}{SafeFileSize(path)}";
    }

    private static string TextInfo(string text, string type)
    {
        var lines = string.IsNullOrEmpty(text) ? 0 : text.Count(character => character == '\n') + 1;
        return $"{type} | {lines} lines | {EditorUtility.FormatBytes(System.Text.Encoding.UTF8.GetByteCount(text))}";
    }

    private static string SafeFileSize(string path)
    {
        try { return File.Exists(path) ? $" | {EditorUtility.FormatBytes(new FileInfo(path).Length)}" : string.Empty; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return string.Empty; }
    }

    private static bool IsTexture(DefaultAsset asset)
    {
        if (asset.assetType.Equals(nameof(Texture), StringComparison.OrdinalIgnoreCase) ||
            asset.assetType.Equals("Image", StringComparison.OrdinalIgnoreCase)) return true;
        return Path.GetExtension(SourcePath(asset)).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp";
    }

    private static bool IsAtlas(DefaultAsset asset) =>
        asset.assetType.Equals(nameof(TextureAtlas), StringComparison.OrdinalIgnoreCase) ||
        SourcePath(asset).EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase);

    private static string IconFor(DefaultAsset asset)
    {
        var path = SourcePath(asset);
        if (string.IsNullOrWhiteSpace(path)) return EditorBuiltinIcons.Assets.Default;
        try { return EditorAssetIcons.GetIconPath(path); }
        catch (ArgumentException) { return EditorBuiltinIcons.Assets.Default; }
    }

    private static string IconForType(BObject asset) =>
        EditorIconRegistry.GetIconPath(asset.GetType()) ?? EditorBuiltinIcons.Assets.Default;

    private static string DisplayName(DefaultAsset asset) =>
        ProjectBrowserPath.DisplayName(asset.name, asset.assetPath, SourcePath(asset));

    private static string SourcePath(DefaultAsset asset) =>
        !string.IsNullOrWhiteSpace(asset.sourcePath) ? ResolvePath(asset.sourcePath) : ResolvePath(asset.assetPath);

    private static string ResolveReference(string reference, string contextPath)
    {
        if (reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
            BAsset.Load<Texture>(reference) is { } texture)
        {
            var importedPath = string.IsNullOrWhiteSpace(texture.artifactPath)
                ? texture.sourcePath
                : texture.artifactPath;
            if (File.Exists(importedPath)) return importedPath;
        }
        if (Path.IsPathRooted(reference)) return ResolvePath(reference);
        try
        {
            var runtimeAssetPath = AssetReferencePath.Resolve(reference);
            if (File.Exists(runtimeAssetPath)) return runtimeAssetPath;
        }
        catch (ArgumentException) { }
        try
        {
            var assetPath = AssetDatabase.ResolveAssetPath(reference);
            if (File.Exists(assetPath)) return assetPath;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or
                                          ArgumentException) { }

        if (!string.IsNullOrWhiteSpace(contextPath))
        {
            var context = ResolvePath(contextPath);
            var combined = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context) ?? string.Empty,
                reference.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(combined)) return combined;
        }
        return ResolvePath(reference);
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        try { return AssetDatabase.ResolveAssetPath(path); }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or
                                          ArgumentException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static string Sanitize(string line)
    {
        if (line.Length == 0) return string.Empty;
        var result = new char[line.Length];
        for (var index = 0; index < line.Length; index++)
            result[index] = line[index] == '\t' ? ' ' : char.IsControl(line[index]) ? ' ' : line[index];
        return new string(result);
    }

    private static bool TryReadImageDimensions(string path, out int width, out int height)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => TryReadPngDimensions(path, out width, out height),
            ".bmp" => TryReadBmpDimensions(path, out width, out height),
            ".jpg" or ".jpeg" => TryReadJpegDimensions(path, out width, out height),
            _ => FailDimensions(out width, out height)
        };
    }

    private static bool TryReadPngDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        Span<byte> header = stackalloc byte[24];
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Read(header) != header.Length ||
                !header[..8].SequenceEqual(PngSignature) ||
                !header.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
            width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4)));
            height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4)));
            return width is > 0 and <= 32768 && height is > 0 and <= 32768;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or OverflowException)
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    private static bool TryReadBmpDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        Span<byte> header = stackalloc byte[26];
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Read(header) != header.Length || !header[..2].SequenceEqual("BM"u8)) return false;
            var dibSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(14, 4));
            if (dibSize == 12)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(18, 2));
                height = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(20, 2));
            }
            else if (dibSize >= 40)
            {
                width = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(18, 4));
                height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(header.Slice(22, 4)));
            }
            return ValidDimensions(width, height);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or OverflowException)
        {
            return FailDimensions(out width, out height);
        }
    }

    private static bool TryReadJpegDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.ReadByte() != 0xff || stream.ReadByte() != 0xd8) return false;
            while (stream.Position < stream.Length)
            {
                int marker;
                do { marker = stream.ReadByte(); } while (marker >= 0 && marker != 0xff);
                if (marker < 0) return false;
                do { marker = stream.ReadByte(); } while (marker == 0xff);
                if (marker < 0 || marker is 0xd9 or 0xda) return false;
                if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
                var segmentLength = ReadUInt16BigEndian(stream);
                if (segmentLength < 2) return false;
                if (IsJpegStartOfFrame(marker))
                {
                    if (segmentLength < 7 || stream.ReadByte() < 0) return false;
                    height = ReadUInt16BigEndian(stream);
                    width = ReadUInt16BigEndian(stream);
                    return ValidDimensions(width, height);
                }
                stream.Seek(segmentLength - 2, SeekOrigin.Current);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            return FailDimensions(out width, out height);
        }
        return false;
    }

    private static bool IsJpegStartOfFrame(int marker) => marker is
        0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or
        0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static int ReadUInt16BigEndian(Stream stream)
    {
        var high = stream.ReadByte();
        var low = stream.ReadByte();
        return high < 0 || low < 0 ? -1 : high << 8 | low;
    }

    private static bool ValidDimensions(int width, int height) =>
        width is > 0 and <= 32768 && height is > 0 and <= 32768;

    private static bool FailDimensions(out int width, out int height)
    {
        width = 0;
        height = 0;
        return false;
    }

    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    private static Rect Fit(Rect area, int width, int height)
    {
        if (width <= 0 || height <= 0) return area;
        var scale = Fix64.Min(area.width / width, area.height / height);
        var fittedWidth = width * scale;
        var fittedHeight = height * scale;
        return new Rect(area.x + (area.width - fittedWidth) * Fix64.Half,
            area.y + (area.height - fittedHeight) * Fix64.Half, fittedWidth, fittedHeight);
    }

    private static Rect Inset(Rect area, Fix64 amount) => new(area.x + amount, area.y + amount,
        Fix64.Max(0, area.width - amount * 2), Fix64.Max(0, area.height - amount * 2));

    private static void DrawBorder(Rect rect, Color color)
    {
        if (rect.width <= 0 || rect.height <= 0) return;
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
        GUI.DrawRect(new Rect(rect.xMax - 1, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
    }

    private readonly record struct FileStamp(bool Exists, long Length, long LastWriteTicks)
    {
        internal static FileStamp Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return default;
                var file = new FileInfo(path);
                return new FileStamp(true, file.Length, file.LastWriteTimeUtc.Ticks);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              ArgumentException) { return default; }
        }
    }

    private readonly record struct CachedImage(FileStamp Stamp, AssetPreviewImage? Image);
    private readonly record struct CachedText(FileStamp Stamp, string Text);
    private readonly record struct CachedAtlas(FileStamp Stamp, AtlasPreviewData? Data);
    private readonly record struct AtlasPreviewData(TextureAtlas Atlas);
}
