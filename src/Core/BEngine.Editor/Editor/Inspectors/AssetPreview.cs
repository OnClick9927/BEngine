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
    internal static event Action<AssetPreviewCacheInvalidation>? CacheInvalidated;

    public static bool HasPreview(BObject? asset) => asset is BAsset or DefaultAsset;

    /// <summary>Returns a source-backed raster preview, or null for assets drawn procedurally.</summary>
    public static AssetPreviewImage? GetAssetPreview(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (AssetPreviewProviders.Find(asset) is { } provider) return provider.GetPreview(asset);
        return asset switch
        {
            DefaultAsset defaultAsset when IsTexture(defaultAsset) => GetImage(SourcePath(defaultAsset)),
            Texture texture => GetImage(texture.sourcePath),
            Sprite sprite => GetSpriteImage(sprite),
            _ => null
        };
    }

    public static string GetMiniThumbnail(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (GetAssetPreview(asset) is { } preview) return preview.Source;
        if (AssetPreviewProviders.Find(asset) is { } provider) return provider.GetIcon(asset);
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
        }
        CacheInvalidated?.Invoke(new AssetPreviewCacheInvalidation(path));
    }

    public static void ClearTemporaryAssetPreviews()
    {
        lock (CacheGate)
        {
            ImageCache.Clear();
            TextCache.Clear();
        }
        CacheInvalidated?.Invoke(new AssetPreviewCacheInvalidation(null));
    }

    /// <summary>Draws either the raster or procedural preview supported for an asset.</summary>
    public static void DrawAssetPreview(BObject asset, Rect previewArea)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (previewArea.width <= 0 || previewArea.height <= 0) return;
        if (AssetPreviewProviders.Find(asset) is { } provider) { provider.Draw(asset, previewArea); return; }

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
        if (AssetPreviewProviders.Find(asset) is { } provider) return provider.GetInfo(asset);
        return asset switch
        {
            DefaultAsset defaultAsset when IsTexture(defaultAsset) => ImageInfo(defaultAsset),
            TextAsset text => TextInfo(GetTextPreview(text), text.assetType),
            AssemblyDefinitionAsset assembly =>
                $"Assembly Definition | {assembly.definition.References.Count} references",
            DefaultAsset defaultAsset => FileInfo(defaultAsset),
            Material material => $"Material | Shader: {material.shader.shaderName}",
            Texture texture => texture.width > 0 && texture.height > 0
                ? $"{texture.width} x {texture.height} | {texture.compressionFormat}"
                : $"Texture | {texture.compressionFormat}",
            Sprite sprite => $"Sprite | {sprite.Texture} | Pivot {sprite.PivotX:0.###}, {sprite.PivotY:0.###}",
            Font font => $"Font | Default size {font.defaultSize} | {font.characterSet}",
            Script script => TextInfo(script.text, "Script"),
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
        var detail = EditorHostServices.FileSystem.DirectoryExists(sourcePath)
            ? "Folder"
            : string.IsNullOrWhiteSpace(asset.assetType) ? "Asset" : asset.assetType;
        DrawIconSummary(area, IconFor(asset), DisplayName(asset), detail);
    }

    internal static AssetPreviewImage? GetSpriteImage(Sprite sprite) => string.IsNullOrWhiteSpace(sprite.Texture) ? null : GetImage(ResolveReference(sprite.Texture, sprite.sourcePath));
    private static void DrawSprite(Sprite sprite, Rect area)
    {
        if (GetSpriteImage(sprite) is { } image) DrawImage(area, image);
        else DrawUnavailable(area, EditorBuiltinIcons.Assets.Image, "Sprite texture unavailable");
    }
    internal static Rect DrawImage(Rect area, AssetPreviewImage image)
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

    internal static void DrawUnavailable(Rect area, string icon, string message) =>
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

    internal static AssetPreviewImage? GetImage(string path)
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
            try { text = EditorHostServices.FileSystem.ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              ArgumentException) { }
        lock (CacheGate) TextCache[path] = new CachedText(stamp, text);
        return text;
    }

    private static string ImageInfo(DefaultAsset asset)
    {
        var image = GetAssetPreview(asset);
        var suffix = SafeFileSize(SourcePath(asset));
        var format = EditorHostServices.Paths.GetExtension(SourcePath(asset)).TrimStart('.').ToUpperInvariant();
        return image is null ? $"{asset.assetType} | Preview unavailable{suffix}" :
            $"{image.Value.Width} x {image.Value.Height} | {format}{suffix}";
    }

    private static string FileInfo(DefaultAsset asset)
    {
        var path = SourcePath(asset);
        if (EditorHostServices.FileSystem.DirectoryExists(path))
        {
            try
            {
                var directoryCount = EditorHostServices.FileSystem.EnumerateDirectories(
                    path, "*", SearchOption.TopDirectoryOnly).Count();
                var fileCount = EditorHostServices.FileSystem.EnumerateFiles(
                    path, "*", SearchOption.TopDirectoryOnly).Count();
                return $"{directoryCount} folders, {fileCount} files";
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
        try
        {
            return EditorHostServices.FileSystem.FileExists(path)
                ? $" | {EditorUtility.FormatBytes(EditorHostServices.FileSystem.GetFileLength(path))}"
                : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return string.Empty; }
    }

    private static bool IsTexture(DefaultAsset asset)
    {
        if (asset.assetType.Equals(nameof(Texture), StringComparison.OrdinalIgnoreCase) ||
            asset.assetType.Equals("Image", StringComparison.OrdinalIgnoreCase)) return true;
        return BEngine.Rendering.ImageCodec.IsSupportedPath(SourcePath(asset));
    }

    internal static string IconFor(DefaultAsset asset)
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

    internal static string SourcePath(DefaultAsset asset) =>
        !string.IsNullOrWhiteSpace(asset.sourcePath) ? ResolvePath(asset.sourcePath) : ResolvePath(asset.assetPath);

    internal static string ResolveReference(string reference, string contextPath)
    {
        if (reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
            BAsset.Load<Texture>(reference) is { } texture)
        {
            var importedPath = string.IsNullOrWhiteSpace(texture.artifactPath)
                ? texture.sourcePath
                : texture.artifactPath;
            if (EditorHostServices.FileSystem.FileExists(importedPath)) return importedPath;
        }
        if (EditorHostServices.Paths.IsPathRooted(reference)) return ResolvePath(reference);
        try
        {
            var runtimeAssetPath = AssetReferencePath.Resolve(reference);
            if (EditorHostServices.FileSystem.FileExists(runtimeAssetPath)) return runtimeAssetPath;
        }
        catch (ArgumentException) { }
        try
        {
            var assetPath = AssetDatabase.ResolveAssetPath(reference);
            if (EditorHostServices.FileSystem.FileExists(assetPath)) return assetPath;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or
                                          ArgumentException) { }

        if (!string.IsNullOrWhiteSpace(contextPath))
        {
            var context = ResolvePath(contextPath);
            var combined = EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.Combine(EditorHostServices.Paths.GetDirectoryName(context) ?? string.Empty,
                reference.Replace('/', EditorHostServices.Paths.DirectorySeparatorChar)));
            if (EditorHostServices.FileSystem.FileExists(combined)) return combined;
        }
        return ResolvePath(reference);
    }

    internal static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (EditorHostServices.Paths.IsPathRooted(path)) return EditorHostServices.Paths.GetFullPath(path);
        try { return AssetDatabase.ResolveAssetPath(path); }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or
                                          ArgumentException)
        {
            return EditorHostServices.Paths.GetFullPath(path);
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
        width = height = 0;
        try { return BEngine.Rendering.ImageCodec.TryGetInfo(
            EditorHostServices.FileSystem.ReadAllBytes(path), out width, out height); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

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

    internal static void DrawBorder(Rect rect, Color color)
    {
        if (rect.width <= 0 || rect.height <= 0) return;
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
        GUI.DrawRect(new Rect(rect.xMax - 1, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
    }

    internal readonly record struct FileStamp(bool Exists, long Length, long LastWriteTicks)
    {
        internal static FileStamp Read(string path)
        {
            try
            {
                if (!EditorHostServices.FileSystem.FileExists(path)) return default;
                return new FileStamp(
                    true,
                    EditorHostServices.FileSystem.GetFileLength(path),
                    EditorHostServices.FileSystem.GetLastWriteTimeUtc(path).Ticks);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              ArgumentException) { return default; }
        }
    }

    private readonly record struct CachedImage(FileStamp Stamp, AssetPreviewImage? Image);
    private readonly record struct CachedText(FileStamp Stamp, string Text);
}
