using BEngine.Rendering;

namespace BEngine.Editor;

public static class TextureAtlasBuilder
{
    private const string Suffix = ".atlas.yaml";

    public static TextureAtlasBuildResult Build(string atlasAssetPath)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Building texture atlases");
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasAssetPath);
        var manifestPath = ResolveManifestPath(atlasAssetPath);
        var atlas = TextureAtlas.Load(manifestPath);
        return Build(atlas, manifestPath);
    }

    public static TextureAtlasBuildResult Build(TextureAtlas atlas, string manifestPath)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Building texture atlases");
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        manifestPath = Path.GetFullPath(manifestPath);
        atlas.Validate();
        if (atlas.SpriteReferences.Count == 0 && atlas.Sources.Count == 0)
            throw new InvalidDataException("Add at least one Sprite before building a texture atlas.");

        var images = atlas.SpriteReferences.Select(reference => LoadSprite(reference, manifestPath))
            .Concat(atlas.Sources.Select(source => LoadLegacySource(source, manifestPath)))
            .OrderByDescending(image => Math.Max(image.Width, image.Height))
            .ThenByDescending(image => image.Width * (long)image.Height)
            .ThenBy(image => image.Source.Name, StringComparer.Ordinal)
            .ToArray();
        RejectDuplicateInputs(images, static image => image.Source.Name, "Sprite name");
        RejectDuplicateInputs(images, static image => image.Source.Reference, "Sprite reference",
            StringComparer.OrdinalIgnoreCase);
        var packed = Pack(images, atlas.Padding, atlas.MaxSize);
        var texturePath = OutputTexturePath(manifestPath);
        if (images.Any(image => image.FullPath.Equals(texturePath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Generated atlas texture '{texturePath}' would overwrite one of its source images.");
        var pixels = new byte[checked(packed.Width * packed.Height * 4)];
        foreach (var placement in packed.Placements)
            CopyWithExtrusion(pixels, packed.Width, packed.Height, placement,
                Math.Min(atlas.Extrude, atlas.Padding));

        Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        File.WriteAllBytes(texturePath, PngImageCodec.EncodeRgba(packed.Width, packed.Height, pixels));
        atlas.Width = packed.Width;
        atlas.Height = packed.Height;
        atlas.Texture = ToPortableAssetPath(texturePath, manifestPath);
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
        if (atlas.SpriteReferences.Count > 0) atlas.Version = 2;
        atlas.Save(manifestPath);
        TextureAtlasResolver.Clear();

        var atlasPath = ToPortableAssetPath(manifestPath, manifestPath);
        var outputPath = ToPortableAssetPath(texturePath, manifestPath);
        if (EditorBridge.Host is not null)
        {
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
        }
        return new TextureAtlasBuildResult(atlasPath, outputPath,
            packed.Width, packed.Height, atlas.Sprites.Count);
    }

    private static SourceImage LoadSprite(string reference, string manifestPath)
    {
        var spritePath = ResolveSourcePath(reference, manifestPath);
        if (!File.Exists(spritePath)) throw new FileNotFoundException(
            $"Texture atlas Sprite '{reference}' does not exist.", spritePath);
        var sprite = Sprite.Load(spritePath);
        var source = new AtlasSource(
            string.IsNullOrWhiteSpace(sprite.name)
                ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(spritePath))
                : sprite.name,
            reference.Replace('\\', '/').Trim(), sprite.Texture, sprite.PivotX, sprite.PivotY);
        return LoadSource(source, spritePath);
    }

    private static SourceImage LoadLegacySource(TextureAtlasSource legacy, string manifestPath) =>
        LoadSource(new AtlasSource(legacy.Name, legacy.Path, legacy.Path,
            legacy.PivotX, legacy.PivotY), manifestPath);

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
            normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
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
            if (directory.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase))
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

    private static string OutputTexturePath(string manifestPath)
    {
        var path = manifestPath.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
            ? manifestPath[..^Suffix.Length]
            : Path.Combine(Path.GetDirectoryName(manifestPath)!, Path.GetFileNameWithoutExtension(manifestPath));
        return path + ".png";
    }

    private sealed record AtlasSource(
        string Name, string Reference, string Texture, float PivotX, float PivotY);
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
