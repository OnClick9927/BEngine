using BEngine.Rendering;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public static class TextureAtlasBuilder
{
    public static TextureAtlasBuildResult Build(string atlasAssetPath)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Building texture atlases");
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasAssetPath);
        var manifestPath = ResolveManifestPath(atlasAssetPath);
        var atlas = TextureAtlas.Load(manifestPath);
        return Build(atlas, manifestPath);
    }

    public static TextureAtlasBuildResult Build(TextureAtlas atlas, string manifestPath)
        => Build(atlas, manifestPath, refreshAssets: true);

    internal static TextureAtlasBuildResult Build(
        TextureAtlas atlas,
        string manifestPath,
        bool refreshAssets)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Building texture atlases");
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        manifestPath = Path.GetFullPath(manifestPath);
        var sources = atlas.Sources.OfType<Sprite>().ToArray();
        if (sources.Length != atlas.Sources.Length) atlas.Sources = sources;
        if (sources.Length == 0)
            return ClearGeneratedTexture(atlas, manifestPath, refreshAssets);
        foreach (var source in sources) BindMissingSourceIdentity(source, manifestPath);

        var images = sources
            .Select(sprite => LoadSprite(sprite, manifestPath))
            .OrderByDescending(image => Math.Max(image.Width, image.Height))
            .ThenByDescending(image => image.Width * (long)image.Height)
            .ThenBy(image => image.Source.Name, StringComparer.Ordinal)
            .ToArray();
        RejectDuplicateInputs(images, static image => image.Source.Name, "Sprite name");
        RejectDuplicateInputs(images, static image => image.Source.Reference, "Sprite reference",
            StringComparer.OrdinalIgnoreCase);
        var packed = Pack(images, atlas.Padding, atlas.MaxSize);
        var pixels = new byte[checked(packed.Width * packed.Height * 4)];
        foreach (var placement in packed.Placements)
            CopyWithExtrusion(pixels, packed.Width, packed.Height, placement,
                Math.Min(atlas.Extrude, atlas.Padding));

        var generated = GeneratedAssetArtifacts.Write(manifestPath, 2800000, ".png",
            PngImageCodec.EncodeRgba(packed.Width, packed.Height, pixels));
        atlas.Width = packed.Width;
        atlas.Height = packed.Height;
        atlas.Texture = generated.Reference;
        atlas.Sprites = packed.Placements.OrderBy(item => item.Image.Source.Name, StringComparer.Ordinal)
            .Select(item => new TextureAtlasSprite
            {
                Name = item.Image.Source.Name,
                Source = item.Image.Source.Reference,
                X = item.X,
                Y = item.Y,
                Width = item.Image.Width,
                Height = item.Image.Height,
                PivotX = item.Image.Source.PivotX,
                PivotY = item.Image.Source.PivotY
            }).ToList();
        atlas.Validate();
        atlas.Save(manifestPath);
        TextureAtlasResolver.Clear();

        var atlasPath = ToPortableAssetPath(manifestPath, manifestPath);
        if (refreshAssets && EditorBridge.Host is not null)
            EditorBridge.Host.RefreshAssets();
        return new TextureAtlasBuildResult(atlasPath, generated.Reference,
            packed.Width, packed.Height, atlas.Sprites.Count);
    }

    private static TextureAtlasBuildResult ClearGeneratedTexture(
        TextureAtlas atlas,
        string manifestPath,
        bool refreshAssets)
    {
        var previousTexture = atlas.Texture;
        var previousSprites = atlas.Sprites;
        var previousWidth = atlas.Width;
        var previousHeight = atlas.Height;
        atlas.Texture = string.Empty;
        atlas.Sprites = [];
        atlas.Width = 0;
        atlas.Height = 0;
        try
        {
            atlas.Validate();
            atlas.Save(manifestPath);
            GeneratedAssetArtifacts.Delete(manifestPath, 2800000);
        }
        catch (Exception exception)
        {
            atlas.Texture = previousTexture;
            atlas.Sprites = previousSprites;
            atlas.Width = previousWidth;
            atlas.Height = previousHeight;
            try { atlas.Save(manifestPath); }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Clearing the generated TextureAtlas artifact failed and its manifest could not be restored.",
                    exception,
                    rollbackException);
            }
            throw;
        }
        TextureAtlasResolver.Clear();
        var atlasPath = ToPortableAssetPath(manifestPath, manifestPath);
        if (refreshAssets && EditorBridge.Host is not null)
            EditorBridge.Host.RefreshAssets();
        return new TextureAtlasBuildResult(atlasPath, string.Empty, 0, 0, 0);
    }

    private static SourceImage LoadSprite(Sprite sprite, string manifestPath)
    {
        var texturePath = ResolveSourcePath(sprite.Texture, manifestPath);
        if (!File.Exists(texturePath))
            throw new FileNotFoundException($"Texture atlas Sprite '{sprite.name}' source Texture does not exist.", texturePath);
        var identity = TextureAtlas.SourceIdentity(sprite);
        var source = new AtlasSource(sprite.name, identity, sprite.Texture,
            sprite.PivotX, sprite.PivotY, sprite.OwnerGuid);
        return LoadSource(source, manifestPath);
    }

    private static void BindMissingSourceIdentity(Sprite sprite, string manifestPath)
    {
        if (Guid.TryParse(sprite.OwnerGuid, out _) && sprite.LocalIdentifier > 0) return;
        var texturePath = ResolveSourcePath(sprite.Texture, manifestPath);
        var metaPath = texturePath + ".meta";
        if (!File.Exists(metaPath))
            throw new InvalidDataException(
                $"Texture atlas Sprite '{sprite.name}' source metadata does not exist: {metaPath}");
        var meta = YamlUtility.Load<AssetMetaDocument>(metaPath);
        if (!Guid.TryParse(meta.Guid, out var ownerGuid))
            throw new InvalidDataException(
                $"Texture atlas Sprite '{sprite.name}' source metadata has an invalid GUID.");
        var localIdentifier = sprite.LocalIdentifier > 0 ? sprite.LocalIdentifier : 21300000;
        sprite.BindSourceIdentity(ownerGuid.ToString("N"), localIdentifier,
            ToPortableAssetPath(texturePath, manifestPath));
    }

    private static SourceImage LoadSource(AtlasSource source, string ownerPath)
    {
        var path = ResolveSourcePath(source.Texture, ownerPath);
        if (!File.Exists(path)) throw new FileNotFoundException(
            $"Texture atlas Sprite texture '{source.Texture}' does not exist.", path);
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Texture atlas Sprite texture '{source.Texture}' must be a PNG file.");
        var bytes = File.ReadAllBytes(path);
        if (!PngImageCodec.TryDecode(bytes, out var width, out var height, out var pixels))
            throw new InvalidDataException($"Texture atlas Sprite texture '{source.Texture}' is not a supported PNG.");
        return new SourceImage(source, path, width, height, pixels);
    }

    private static void RejectDuplicateInputs(
        IEnumerable<SourceImage> images,
        Func<SourceImage, string> selector,
        string label,
        IEqualityComparer<string>? comparer = null)
    {
        var duplicate = images.GroupBy(selector, comparer ?? StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Texture atlas {label} '{duplicate.Key}' is duplicated.");
    }

    private static PackedAtlas Pack(IReadOnlyList<SourceImage> images, int padding, int maxSize)
    {
        var requiredArea = images.Sum(image => checked((long)(image.Width + padding * 2) *
                                                             (image.Height + padding * 2)));
        var minimumWidth = images.Max(image => checked(image.Width + padding * 2));
        var minimumHeight = images.Max(image => checked(image.Height + padding * 2));
        var candidates = PowersOfTwo(32, maxSize)
            .SelectMany(width => PowersOfTwo(32, maxSize).Select(height => (Width: width, Height: height)))
            .Where(size => size.Width >= minimumWidth && size.Height >= minimumHeight &&
                           (long)size.Width * size.Height >= requiredArea)
            .OrderBy(size => (long)size.Width * size.Height)
            .ThenBy(size => Math.Max(size.Width, size.Height))
            .ThenBy(size => size.Width)
            .ToArray();
        foreach (var size in candidates)
            if (TryPack(images, padding, size.Width, size.Height, out var placements))
                return new PackedAtlas(size.Width, size.Height, placements);
        throw new InvalidDataException($"Texture atlas sources do not fit within {maxSize}x{maxSize}.");
    }

    private static bool TryPack(IReadOnlyList<SourceImage> images, int padding,
        int width, int height, out List<Placement> placements)
    {
        var free = new List<IntRect> { new(0, 0, width, height) };
        placements = [];
        foreach (var image in images)
        {
            var outerWidth = checked(image.Width + padding * 2);
            var outerHeight = checked(image.Height + padding * 2);
            var choice = free.Where(rect => outerWidth <= rect.Width && outerHeight <= rect.Height)
                .Select(rect => new
                {
                    Rect = rect,
                    Short = Math.Min(rect.Width - outerWidth, rect.Height - outerHeight),
                    Long = Math.Max(rect.Width - outerWidth, rect.Height - outerHeight)
                })
                .OrderBy(item => item.Short).ThenBy(item => item.Long)
                .ThenBy(item => item.Rect.Y).ThenBy(item => item.Rect.X)
                .FirstOrDefault();
            if (choice is null) return false;
            var used = new IntRect(choice.Rect.X, choice.Rect.Y, outerWidth, outerHeight);
            SplitFreeRectangles(free, used);
            placements.Add(new Placement(image, used.X + padding, used.Y + padding));
        }
        return true;
    }

    private static void SplitFreeRectangles(List<IntRect> free, IntRect used)
    {
        var additions = new List<IntRect>();
        for (var index = free.Count - 1; index >= 0; index--)
        {
            var item = free[index];
            if (!item.Intersects(used)) continue;
            free.RemoveAt(index);
            if (used.X > item.X) additions.Add(new IntRect(item.X, item.Y, used.X - item.X, item.Height));
            if (used.Right < item.Right)
                additions.Add(new IntRect(used.Right, item.Y, item.Right - used.Right, item.Height));
            if (used.Y > item.Y) additions.Add(new IntRect(item.X, item.Y, item.Width, used.Y - item.Y));
            if (used.Bottom < item.Bottom)
                additions.Add(new IntRect(item.X, used.Bottom, item.Width, item.Bottom - used.Bottom));
        }
        free.AddRange(additions.Where(rect => rect.Width > 0 && rect.Height > 0));
        for (var left = free.Count - 1; left >= 0; left--)
        for (var right = free.Count - 1; right >= 0; right--)
        {
            if (left == right || !free[right].Contains(free[left])) continue;
            free.RemoveAt(left);
            break;
        }
    }

    private static void CopyWithExtrusion(byte[] destination, int atlasWidth, int atlasHeight,
        Placement placement, int extrude)
    {
        var image = placement.Image;
        for (var y = -extrude; y < image.Height + extrude; y++)
        for (var x = -extrude; x < image.Width + extrude; x++)
        {
            var targetX = placement.X + x;
            var targetY = placement.Y + y;
            if (targetX < 0 || targetX >= atlasWidth || targetY < 0 || targetY >= atlasHeight) continue;
            var sourceX = Math.Clamp(x, 0, image.Width - 1);
            var sourceY = Math.Clamp(y, 0, image.Height - 1);
            var sourceOffset = (sourceY * image.Width + sourceX) * 4;
            var targetOffset = (targetY * atlasWidth + targetX) * 4;
            image.Pixels.AsSpan(sourceOffset, 4).CopyTo(destination.AsSpan(targetOffset, 4));
        }
    }

    private static IEnumerable<int> PowersOfTwo(int minimum, int maximum)
    {
        for (var value = NextPowerOfTwo(minimum); value <= maximum; value = checked(value * 2))
        {
            yield return value;
            if (value > maximum / 2) yield break;
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value) result = checked(result * 2);
        return result;
    }

    private static string ResolveManifestPath(string path) => Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : AssetDatabase.ResolveAssetPath(path);

    private static string ResolveSourcePath(string path, string manifestPath)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Packages", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Packages{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var projectRoot = FindProjectRoot(manifestPath);
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, normalized));
    }

    private static string FindProjectRoot(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null;
             directory = directory.Parent)
            if (directory.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.Equals("Packages", StringComparison.OrdinalIgnoreCase))
                return directory.Parent?.FullName ?? directory.FullName;
        return EditorBridge.Host?.ProjectRootPath ?? Path.GetDirectoryName(path)!;
    }

    private static string ToPortableAssetPath(string path, string manifestPath)
    {
        var projectRoot = FindProjectRoot(manifestPath);
        if (path.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(projectRoot, path).Replace('\\', '/');
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    private sealed record AtlasSource(
        string Name, string Reference, string Texture, float PivotX, float PivotY, string Guid);
    private sealed record SourceImage(
        AtlasSource Source, string FullPath, int Width, int Height, byte[] Pixels);
    private sealed record Placement(SourceImage Image, int X, int Y);
    private sealed record PackedAtlas(int Width, int Height, List<Placement> Placements);
    private readonly record struct IntRect(int X, int Y, int Width, int Height)
    {
        internal int Right => X + Width;
        internal int Bottom => Y + Height;
        internal bool Intersects(IntRect other) =>
            other.Right > X && other.X < Right && other.Bottom > Y && other.Y < Bottom;
        internal bool Contains(IntRect other) =>
            other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
    }
}
