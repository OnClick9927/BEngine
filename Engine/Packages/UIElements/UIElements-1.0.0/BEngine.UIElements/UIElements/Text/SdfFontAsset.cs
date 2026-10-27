using System.Text;
using System.Text.Json;

namespace BEngine.UIElements;

/// <summary>A baked, platform independent font atlas. The distance contour is 0.5; positive is inside.</summary>
[EditorIcon("Icons/Assets/AssetFont.png")]
public sealed class SdfFontAsset : ScriptableObject
{
    private SdfFontData _data = new();
    private Dictionary<int, SdfGlyph> _glyphs = [];
    private AdvancedFontRuntime? _advancedRuntime;
    internal string textureKey { get; } = "sdf:" + Guid.NewGuid().ToString("N");
    public string fontFamily => _data.FontFamily;
    public int samplingSize => _data.SamplingSize;
    public float lineHeight => _data.LineHeight;
    public int distanceRange => _data.DistanceRange;
    public int atlasWidth => _data.Width;
    public int atlasHeight => _data.Height;
    public int glyphCount => _glyphs.Count;
    public bool supportsAdvancedText => _data.Version >= 2 && _data.SourceFonts.Length > 0;
    public int embeddedFontCount => _data.SourceFonts.Length;
    public IReadOnlyList<string> embeddedFontNames => _data.SourceFonts.Select(source => source.Name).ToArray();
    public string characters => string.Concat(_glyphs.Keys.Order().Select(codepoint => new Rune(codepoint).ToString()));
    public ReadOnlyMemory<byte> distancePixels { get { ThrowIfDestroyed(); return _data.Pixels; } }

    public bool TryGetGlyph(int codepoint, out SdfGlyph glyph) => _glyphs.TryGetValue(codepoint, out glyph);
    internal SdfGlyph Glyph(int codepoint) => _glyphs.GetValueOrDefault(codepoint, _glyphs['?']);
    public float MeasureAdvance(string text, float size)
    {
        ArgumentNullException.ThrowIfNull(text); ThrowIfDestroyed();
        if (supportsAdvancedText) return AdvancedRuntime.Measure(text, size, 0, false, TextDirectionMode.Auto).Width;
        using var budget = LoopExecutionGuard.AllowIterations(text.Length + 1);
        var line = 0f; var maximum = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\r') continue;
            if (rune.Value == '\n') { maximum = Math.Max(maximum, line); line = 0; continue; }
            line += rune.Value == '\t' ? Glyph(' ').Advance * 4 : Glyph(rune.Value).Advance;
        }
        return Math.Max(line, maximum) * Math.Max(0, size) / samplingSize;
    }
    /// <summary>Shapes text with OpenType substitutions, positioning, bidi and the embedded fallback chain.</summary>
    public AdvancedTextMetrics MeasureAdvanced(string text, float size, float wrappingWidth = 0,
        bool wrap = false, TextDirectionMode direction = TextDirectionMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(text); ThrowIfDestroyed();
        if (!supportsAdvancedText) throw new InvalidOperationException("This font asset does not contain embedded OpenType fonts.");
        return AdvancedRuntime.Measure(text, size, wrappingWidth, wrap, direction);
    }
    public byte[] ToBytes() { ThrowIfDestroyed(); return JsonSerializer.SerializeToUtf8Bytes(_data); }
    public void Save(string path)
    {
        EnsureAssetCanBeSaved(); ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RuntimeServiceAccess.FileSystem.WriteAllBytes(path, ToBytes());
        BindAssetReference(path); Invalidate(path);
    }
    public static SdfFontAsset FromBytes(ReadOnlySpan<byte> bytes, string path = "")
    {
        return Create(ReadData(bytes), path);
    }
    /// <summary>Compiles JSON authoring data into the deterministic, bounded runtime font format.</summary>
    /// <remarks>All atlas, glyph and embedded shaping data are retained. Save and ToBytes still produce JSON.</remarks>
    public static byte[] CompileRuntimeBytes(ReadOnlySpan<byte> bytes) => SdfFontBinaryCodec.Write(ReadData(bytes));

    private static SdfFontData ReadData(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > SdfFontBinaryCodec.MaximumDocumentBytes)
            throw new InvalidDataException("Font asset exceeds the 192 MB asset limit.");
        return SdfFontBinaryCodec.IsBinary(bytes) ? SdfFontBinaryCodec.Read(bytes) :
            JsonSerializer.Deserialize<SdfFontData>(bytes) ?? throw new InvalidDataException("SDF font is empty.");
    }
    internal static SdfFontAsset Create(SdfFontData data, string path = "")
    {
        data.Validate();
        var font = new SdfFontAsset { _data = data, _glyphs = data.Glyphs.ToDictionary(glyph => glyph.Codepoint) };
        if (path.Length > 0) font.BindAssetReference(path);
        return font;
    }
    public static SdfFontAsset CreateDynamic(string name, ReadOnlySpan<byte> primaryFont,
        IEnumerable<DynamicFontSource>? fallbackFonts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (primaryFont.IsEmpty) throw new ArgumentException("The primary font file is empty.", nameof(primaryFont));
        var sources = new List<DynamicFontSource> { new(name.Trim(), primaryFont.ToArray()) };
        if (fallbackFonts is not null) sources.AddRange(fallbackFonts);
        var data = new SdfFontData
        {
            Version = 2, FontFamily = name.Trim(), SamplingSize = 64, LineHeight = 76, DistanceRange = 8,
            Width = 1, Height = 1, Pixels = [0],
            Glyphs = [new(' ', 0, 0, 0, 0, 32, 0, 0), new('?', 0, 0, 0, 0, 32, 0, 0)],
            SourceFonts = sources.ToArray()
        };
        var asset = Create(data);
        _ = asset.AdvancedRuntime;
        return asset;
    }
    internal AdvancedFontRuntime AdvancedRuntime => _advancedRuntime ??= new AdvancedFontRuntime(_data.SourceFonts);
    protected override object CaptureClonePayload() => ToBytes();
    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        var data = JsonSerializer.Deserialize<SdfFontData>((byte[])payload!) ?? throw new InvalidDataException("SDF clone is empty.");
        data.Validate(); _data = data; _glyphs = data.Glyphs.ToDictionary(glyph => glyph.Codepoint); _advancedRuntime = null;
    }
    protected override void ReleaseAssetResources() { _advancedRuntime?.Dispose(); _advancedRuntime = null; _data = new(); _glyphs.Clear(); }
}

public readonly record struct SdfGlyph(int Codepoint, int X, int Y, int Width, int Height,
    float Advance, float BearingX, float BearingY);

public readonly record struct DynamicFontSource(string Name, byte[] Data);

public readonly record struct AdvancedTextMetrics(float Width, float Height, int GlyphCount, int GraphemeCount,
    int UsedFontCount, bool AppliedOpenTypeSubstitutions, TextDirectionMode ResolvedDirection);

internal sealed class SdfFontData
{
    public int Version { get; set; } = 1;
    public string FontFamily { get; set; } = "";
    public int SamplingSize { get; set; }
    public float LineHeight { get; set; }
    public int DistanceRange { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Pixels { get; set; } = [];
    public SdfGlyph[] Glyphs { get; set; } = [];
    public DynamicFontSource[] SourceFonts { get; set; } = [];
    public void Validate()
    {
        if (Version is not (1 or 2) || FontFamily is null || SamplingSize is < 16 or > 256 ||
            !float.IsFinite(LineHeight) || LineHeight <= 0 || LineHeight > 1024 || DistanceRange is < 2 or > 32 ||
            Width is < 1 or > 4096 || Height is < 1 or > 4096 || Pixels is null || Pixels.Length != Width * Height ||
            Glyphs is null || Glyphs.Length is < 2 or > 8192)
            throw new InvalidDataException("Invalid SDF font metadata or atlas.");
        if (SourceFonts is null || (Version == 1 && SourceFonts.Length != 0) || SourceFonts.Length > 32)
            throw new InvalidDataException("Invalid embedded font chain.");
        long fontBytes = 0;
        foreach (var source in SourceFonts)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 256 || source.Data is null ||
                source.Data.Length is < 12 or > 64 * 1024 * 1024)
                throw new InvalidDataException("Invalid embedded font source.");
            fontBytes += source.Data.Length;
        }
        if (Version == 2 && SourceFonts.Length == 0 || fontBytes > 128L * 1024 * 1024)
            throw new InvalidDataException("Advanced font assets require a valid embedded font chain under 128 MB.");
        var seen = new HashSet<int>();
        using var budget = LoopExecutionGuard.AllowIterations(Glyphs.Length + 1);
        foreach (var glyph in Glyphs)
            if (!Rune.IsValid(glyph.Codepoint) || !seen.Add(glyph.Codepoint) || glyph.X < 0 || glyph.Y < 0 ||
                glyph.Width < 0 || glyph.Height < 0 || glyph.Width > Width || glyph.Height > Height ||
                glyph.X > Width - glyph.Width || glyph.Y > Height - glyph.Height ||
                !float.IsFinite(glyph.Advance) || glyph.Advance < 0 || glyph.Advance > 1024 ||
                !float.IsFinite(glyph.BearingX) || !float.IsFinite(glyph.BearingY) ||
                Math.Abs(glyph.BearingX) > 1024 || Math.Abs(glyph.BearingY) > 1024)
                throw new InvalidDataException("Invalid or duplicate SDF glyph.");
        if (!seen.Contains('?') || !seen.Contains(' ')) throw new InvalidDataException("SDF font requires '?' and space fallback glyphs.");
    }
}
