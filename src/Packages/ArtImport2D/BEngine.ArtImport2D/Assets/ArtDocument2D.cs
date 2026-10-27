using BEngine.Serialization;

namespace BEngine;

public enum ArtDocumentSourceKind { Photoshop, Aseprite }
public enum AsepriteTagDirection { Forward, Reverse, PingPong, PingPongReverse }

public sealed class ArtLayer2D
{
    public int StableId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte Opacity { get; set; } = 255;
    public bool Visible { get; set; } = true;
    public long LocalIdentifier { get; set; }
    public string TextureReference { get; set; } = string.Empty;
    public int ParentIndex { get; set; } = -1;
    public bool IsGroup { get; set; }
    public string BlendMode { get; set; } = "normal";
    public List<ArtPrecisionChannel> PrecisionChannels { get; set; } = [];
    public Texture? LoadTexture() => BAsset.Load<Texture>(TextureReference);
}

public sealed class ArtFrame2D
{
    public int Index { get; set; }
    public int DurationMilliseconds { get; set; }
    public long LocalIdentifier { get; set; }
    public string TextureReference { get; set; } = string.Empty;
    public Texture? LoadTexture() => BAsset.Load<Texture>(TextureReference);
}

public sealed class AsepriteTag2D
{
    public string Name { get; set; } = string.Empty;
    public int FromFrame { get; set; }
    public int ToFrame { get; set; }
    public AsepriteTagDirection Direction { get; set; }
    public int Repeat { get; set; }
}

[EditorIcon("Icons/Assets/AssetImage.png")]
public sealed class ArtDocument2D : BAsset
{
    public string Format { get; set; } = "BEngine.ArtDocument2D";
    public int Version { get; set; } = 1;
    public ArtDocumentSourceKind SourceKind { get; set; }
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public List<ArtLayer2D> Layers { get; set; } = [];
    public List<ArtFrame2D> Frames { get; set; } = [];
    public List<AsepriteTag2D> Tags { get; set; } = [];
    public int SourceBitDepth { get; set; } = 8;
    public List<ArtSourceMetadata> SourceMetadata { get; set; } = [];
    public List<AsepriteSlice2D> Slices { get; set; } = [];
    public List<string> SourceDependencies { get; set; } = [];

    public ArtLayer2D? FindLayer(string name) =>
        Layers.FirstOrDefault(item => item.Name.Equals(name, StringComparison.Ordinal));
    public AsepriteTag2D? FindTag(string name) =>
        Tags.FirstOrDefault(item => item.Name.Equals(name, StringComparison.Ordinal));

    public void Validate()
    {
        if (!Format.Equals("BEngine.ArtDocument2D", StringComparison.Ordinal) || Version != 1)
            throw new InvalidDataException($"Unsupported art document format '{Format}' version {Version}.");
        if (CanvasWidth is < 1 or > 16384 || CanvasHeight is < 1 or > 16384)
            throw new InvalidDataException("Art document canvas dimensions must be between 1 and 16384.");
        Layers ??= []; Frames ??= []; Tags ??= []; SourceMetadata ??= []; Slices ??= [];
        // Earlier Aseprite artifacts retained parent links before an explicit group flag existed.
        if (SourceKind == ArtDocumentSourceKind.Aseprite)
            foreach (var item in Layers)
                if (item.ParentIndex >= 0 && item.ParentIndex < Layers.Count && Layers[item.ParentIndex] is { LocalIdentifier: 0, TextureReference.Length: 0 } parent)
                    parent.IsGroup = true;
        var identities = new HashSet<long>();
        foreach (var layer in Layers)
        {
            layer.Name = layer.Name?.Trim() ?? string.Empty;
            layer.TextureReference = Normalize(layer.TextureReference);
            var hasTexture = layer.LocalIdentifier > 0 || layer.TextureReference.Length > 0;
            if (layer.Name.Length == 0 || layer.Width < 0 || layer.Height < 0 ||
                hasTexture && (layer.LocalIdentifier <= 0 || layer.TextureReference.Length == 0 || !identities.Add(layer.LocalIdentifier)) ||
                SourceKind == ArtDocumentSourceKind.Photoshop && !layer.IsGroup && !hasTexture || layer.IsGroup && hasTexture)
                throw new InvalidDataException($"Layer '{layer.Name}' has invalid dimensions or identity.");
            var visited = new HashSet<int>();
            var parent = layer.ParentIndex;
            while (parent >= 0)
            {
                if (parent >= Layers.Count || ReferenceEquals(Layers[parent], layer) || !visited.Add(parent) || !Layers[parent].IsGroup)
                    throw new InvalidDataException($"Layer '{layer.Name}' has an invalid or cyclic group hierarchy.");
                parent = Layers[parent].ParentIndex;
            }
        }
        foreach (var frame in Frames)
        {
            frame.TextureReference = Normalize(frame.TextureReference);
            if (frame.Index < 0 || frame.DurationMilliseconds < 1 || frame.LocalIdentifier <= 0 ||
                frame.TextureReference.Length == 0 || !identities.Add(frame.LocalIdentifier))
                throw new InvalidDataException($"Frame {frame.Index} has invalid duration or identity.");
        }
        foreach (var tag in Tags)
        {
            tag.Name = tag.Name?.Trim() ?? string.Empty;
            if (tag.Name.Length == 0 || tag.FromFrame < 0 || tag.ToFrame < tag.FromFrame ||
                tag.ToFrame >= Frames.Count || !Enum.IsDefined(tag.Direction))
                throw new InvalidDataException($"Aseprite tag '{tag.Name}' has an invalid frame range.");
        }
    }

    public string Serialize()
    {
        Validate();
        return YamlUtility.Serialize(new ArtDocument2DData
        {
            Format = Format, Version = Version, SourceKind = SourceKind,
            CanvasWidth = CanvasWidth, CanvasHeight = CanvasHeight,
            Layers = Layers, Frames = Frames, Tags = Tags, SourceBitDepth = SourceBitDepth, SourceMetadata = SourceMetadata,
            Slices = Slices.Select(ArtSliceData.From).ToList(), SourceDependencies = SourceDependencies
        });
    }

    public static ArtDocument2D Deserialize(string yaml)
    {
        var data = YamlUtility.Deserialize<ArtDocument2DData>(yaml);
        var document = new ArtDocument2D
        {
            Format = data.Format, Version = data.Version, SourceKind = data.SourceKind,
            CanvasWidth = data.CanvasWidth, CanvasHeight = data.CanvasHeight,
            Layers = data.Layers ?? [], Frames = data.Frames ?? [], Tags = data.Tags ?? [],
            SourceBitDepth = data.SourceBitDepth, SourceMetadata = data.SourceMetadata ?? [], Slices = (data.Slices ?? []).Select(slice => slice.ToSlice()).ToList(), SourceDependencies = data.SourceDependencies ?? []
        };
        document.Validate();
        return document;
    }

    public static ArtDocument2D LoadImported(string path)
    {
        var document = Deserialize(File.ReadAllText(path));
        document.name = Path.GetFileNameWithoutExtension(path);
        return document;
    }

    private static string Normalize(string? path) => path?.Replace('\\', '/').Trim() ?? string.Empty;
}

internal sealed class ArtDocument2DData
{
    public string Format { get; set; } = "BEngine.ArtDocument2D";
    public int Version { get; set; } = 1;
    public ArtDocumentSourceKind SourceKind { get; set; }
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public List<ArtLayer2D> Layers { get; set; } = [];
    public List<ArtFrame2D> Frames { get; set; } = [];
    public List<AsepriteTag2D> Tags { get; set; } = [];
    public int SourceBitDepth { get; set; } = 8;
    public List<ArtSourceMetadata> SourceMetadata { get; set; } = [];
    public List<ArtSliceData> Slices { get; set; } = [];
    public List<string> SourceDependencies { get; set; } = [];
}

internal sealed class ArtSliceData
{
    public string Name { get; set; } = "";
    public List<ArtSliceKeyData> Keys { get; set; } = [];
    public static ArtSliceData From(AsepriteSlice2D slice) => new() { Name = slice.Name, Keys = slice.Keys.Select(key => new ArtSliceKeyData
    { Frame = key.Frame, Bounds = Pack(key.Bounds), Center = key.Center is { } center ? Pack(center) : null, Pivot = key.Pivot is { } pivot ? [pivot.x, pivot.y] : null }).ToList() };
    private static Fix64[] Pack(Rect rect) => [rect.x, rect.y, rect.width, rect.height];
    public AsepriteSlice2D ToSlice() => new() { Name = Name, Keys = Keys.Select(key => new AsepriteSliceKey2D
    { Frame = key.Frame, Bounds = Unpack(key.Bounds), Center = key.Center is { } center ? Unpack(center) : null,
      Pivot = key.Pivot is { Length: 2 } pivot ? new Vector2(pivot[0], pivot[1]) : null }).ToList() };
    private static Rect Unpack(Fix64[] values) => values.Length == 4 ? new(values[0], values[1], values[2], values[3]) : throw new InvalidDataException("Slice bounds require four coordinates.");
}
internal sealed class ArtSliceKeyData
{
    public int Frame { get; set; }
    public Fix64[] Bounds { get; set; } = [];
    public Fix64[]? Center { get; set; }
    public Fix64[]? Pivot { get; set; }
}
