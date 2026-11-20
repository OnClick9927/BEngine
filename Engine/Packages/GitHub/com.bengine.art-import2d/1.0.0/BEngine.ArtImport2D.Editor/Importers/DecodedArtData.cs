namespace BEngine.Editor;

internal sealed class DecodedArtLayer
{
    internal required int StableId { get; init; }
    internal required string Name { get; init; }
    internal required int X { get; init; }
    internal required int Y { get; init; }
    internal required int Width { get; init; }
    internal required int Height { get; init; }
    internal required byte Opacity { get; init; }
    internal required bool Visible { get; init; }
    internal required byte[] Rgba { get; init; }
    internal int ParentIndex { get; set; } = -1;
    internal bool IsGroup { get; init; }
    internal string BlendMode { get; init; } = "normal";
    internal List<ArtPrecisionChannel> PrecisionChannels { get; init; } = [];
}

internal sealed class DecodedArtFrame
{
    internal required int Index { get; init; }
    internal required int DurationMilliseconds { get; init; }
    internal required byte[] Rgba { get; init; }
}

internal sealed class DecodedAsepriteTag
{
    internal required string Name { get; init; }
    internal required int From { get; init; }
    internal required int To { get; init; }
    internal required AsepriteTagDirection Direction { get; init; }
    internal required int Repeat { get; init; }
}

internal sealed class DecodedArtDocument
{
    internal required ArtDocumentSourceKind Kind { get; init; }
    internal required int Width { get; init; }
    internal required int Height { get; init; }
    internal List<DecodedArtLayer> Layers { get; } = [];
    internal List<DecodedArtFrame> Frames { get; } = [];
    internal List<DecodedAsepriteTag> Tags { get; } = [];
    internal int SourceBitDepth { get; set; } = 8;
    internal List<ArtSourceMetadata> Metadata { get; } = [];
    internal List<AsepriteSlice2D> Slices { get; } = [];
    internal List<string> SourceDependencies { get; } = [];
}
