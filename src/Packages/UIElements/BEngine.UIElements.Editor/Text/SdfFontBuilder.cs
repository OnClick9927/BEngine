using System.Text;
using BEngine.Rendering;
using BEngine.UIElements;

namespace BEngine.Editor;

public static class SdfFontBuilder
{
    public const string DefaultCharacters = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    /// <summary>Bakes the selected characters once. Players render the atlas without installed fonts.</summary>
    public static SdfFontAsset Bake(string characters, string family = "", int samplingSize = 64, int distanceRange = 8)
    {
        var rasterizer = RuntimeServiceAccess.GetService<ITextRasterizer>() ??
            throw new NotSupportedException("This platform does not provide font rasterization.");
        if (!string.IsNullOrWhiteSpace(family)) rasterizer = rasterizer.ForFontFamily(family) ??
            throw new NotSupportedException("This platform cannot select a font family.");
        return Bake(rasterizer, characters, family, samplingSize, distanceRange);
    }

    /// <summary>Embeds OpenType files for runtime HarfBuzz shaping, bidi, fallback and dynamic glyph rendering.</summary>
    public static SdfFontAsset CreateDynamic(string primaryPath, IEnumerable<string>? fallbackPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryPath);
        static DynamicFontSource Read(string path)
        {
            if (!path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Dynamic fonts must use a .ttf, .otf or .ttc source file.");
            var bytes = RuntimeServiceAccess.FileSystem.ReadAllBytes(path);
            return new(Path.GetFileNameWithoutExtension(path), bytes);
        }
        var primary = Read(primaryPath);
        var fallbacks = (fallbackPaths ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).Select(Read).ToArray();
        return SdfFontAsset.CreateDynamic(primary.Name, primary.Data, fallbacks);
    }

    internal static SdfFontAsset Bake(ITextRasterizer rasterizer, string characters, string family, int size, int range)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (size is < 16 or > 256 || range is < 2 or > 32) throw new ArgumentOutOfRangeException(nameof(size), "Sampling size: 16–256; distance range: 2–32.");
        var runes = (characters + " ?").EnumerateRunes().Where(r => !Rune.IsControl(r)).Distinct().OrderBy(r => r.Value).ToArray();
        if (runes.Length > 8192) throw new ArgumentException("At most 8192 characters fit in one font asset.");
        using var budget = LoopExecutionGuard.AllowIterations(4096 * 4096);
        if (!rasterizer.TryMeasureText("Mg", size, out _, out var lineHeight) || !float.IsFinite(lineHeight) || lineHeight <= 0)
            throw new InvalidOperationException("Font measurement failed.");
        var padding = rasterizer.GetHorizontalPadding(size);
        var sourceHeight = (int)Math.Ceiling(lineHeight);
        var glyphs = new List<(SdfGlyph Glyph, byte[] Pixels)>();
        foreach (var rune in runes)
        {
            var text = rune.ToString();
            if (!rasterizer.TryMeasureText(text, size, out var advance, out _) || !float.IsFinite(advance) || advance < 0 || advance > 1024)
                throw new InvalidOperationException($"Cannot measure U+{rune.Value:X}.");
            if (Rune.IsWhiteSpace(rune)) { glyphs.Add((new(rune.Value, 0, 0, 0, 0, advance, 0, 0), [])); continue; }
            var sourceWidth = Math.Max(1, (int)Math.Ceiling(advance + padding));
            if (!rasterizer.TryResolveText(text, sourceWidth, sourceHeight, size, out var mask))
                throw new InvalidOperationException($"Cannot rasterize U+{rune.Value:X}.");
            mask.Validate();
            if (mask.Width != sourceWidth || mask.Height != sourceHeight) throw new InvalidOperationException("Font rasterizer changed the requested cell size.");
            var width = sourceWidth + range * 2; var height = sourceHeight + range * 2;
            var alpha = new byte[width * height];
            for (var y = 0; y < sourceHeight; y++)
                for (var x = 0; x < sourceWidth; x++) alpha[(y + range) * width + x + range] = mask.Pixels.Span[(y * sourceWidth + x) * 4 + 3];
            // The rasterizer reserves padding around the advance; two extra pixels are end guards.
            var inset = Math.Max(0, (padding - 2) * .5f);
            glyphs.Add((new(rune.Value, 0, 0, width, height, advance, -range - inset, -range),
                SdfDistanceField.Generate(alpha, width, height, range)));
        }
        var area = glyphs.Sum(item => (long)(item.Glyph.Width + 1) * (item.Glyph.Height + 1));
        var atlasWidth = PowerOfTwo((int)Math.Ceiling(Math.Sqrt(area)));
        atlasWidth = Math.Clamp(Math.Max(atlasWidth, PowerOfTwo(glyphs.Max(item => item.Glyph.Width + 1))), 64, 4096);
        var packed = new List<(SdfGlyph Glyph, byte[] Pixels)>(); var x0 = 0; var y0 = 0; var rowHeight = 0;
        foreach (var item in glyphs)
        {
            var glyph = item.Glyph;
            if (glyph.Width == 0) { packed.Add(item); continue; }
            if (x0 + glyph.Width > atlasWidth) { y0 += rowHeight; x0 = 0; rowHeight = 0; }
            packed.Add((glyph with { X = x0, Y = y0 }, item.Pixels));
            x0 += glyph.Width + 1; rowHeight = Math.Max(rowHeight, glyph.Height + 1);
        }
        // SDF assets and supported graphics backends accept rectangular NPOT
        // atlases. Keep the same cells/padding and avoid doubling the entire
        // texture when the last packed row barely crosses a power of two.
        var usedHeight = y0 + rowHeight;
        var atlasHeight = Math.Min(PowerOfTwo(usedHeight), checked((usedHeight + 63) / 64 * 64));
        if (atlasHeight > 4096) throw new InvalidOperationException("Font exceeds 4096×4096. Reduce the character set or sampling size.");
        var pixels = new byte[atlasWidth * atlasHeight];
        foreach (var (glyph, source) in packed)
            for (var row = 0; row < glyph.Height; row++)
                Array.Copy(source, row * glyph.Width, pixels, (glyph.Y + row) * atlasWidth + glyph.X, glyph.Width);
        return SdfFontAsset.Create(new SdfFontData { FontFamily = family, SamplingSize = size, DistanceRange = range,
            LineHeight = lineHeight, Width = atlasWidth, Height = atlasHeight, Pixels = pixels, Glyphs = packed.Select(item => item.Glyph).ToArray() });
    }
    private static int PowerOfTwo(int value) { var result = 1; while (result < value) result *= 2; return result; }
}
