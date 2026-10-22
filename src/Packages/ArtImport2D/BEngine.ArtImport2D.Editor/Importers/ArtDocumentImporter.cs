using System.Security.Cryptography;
using System.Text;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

[AssetImporterVersion(4)]
public sealed class ArtDocumentImporter : AssetImporter
{
    public bool importHiddenLayers { get; set; } = true;
    public bool makeTexturesReadable { get; set; } = true;
    public override IEnumerable<string> GetSourceDependencies(string sourcePath) => Path.GetExtension(sourcePath).ToLowerInvariant() is ".ase" or ".aseprite"
        ? AsepriteDecoder.GetSourceDependencies(sourcePath) : [];

    public override void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var extension = Path.GetExtension(context.sourcePath).ToLowerInvariant();
        var decoded = extension is ".psd" or ".psb"
            ? PhotoshopDecoder.Decode(context.ReadSourceBytes())
            : extension is ".aseprite" or ".ase"
                ? AsepriteDecoder.Decode(context.ReadSourceBytes(), context.sourcePath)
                : throw new InvalidDataException($"Unsupported art source extension '{extension}'.");
        var document = new ArtDocument2D
        {
            name = Path.GetFileNameWithoutExtension(context.sourcePath),
            SourceKind = decoded.Kind,
            CanvasWidth = decoded.Width,
            CanvasHeight = decoded.Height, SourceBitDepth = decoded.SourceBitDepth,
            SourceMetadata = decoded.Metadata, Slices = decoded.Slices,
            SourceDependencies = decoded.SourceDependencies
        };
        var usedIds = new HashSet<long>();
        var layerIndices = new Dictionary<int, int>();
        for (var index = 0; index < decoded.Layers.Count; index++)
        {
            var source = decoded.Layers[index];
            if (!importHiddenLayers && !IsVisible(index)) continue;
            var layer = new ArtLayer2D
            {
                StableId = source.StableId, Name = source.Name, X = source.X, Y = source.Y,
                Width = source.Width, Height = source.Height, Opacity = source.Opacity, Visible = source.Visible,
                ParentIndex = source.ParentIndex, IsGroup = source.IsGroup, BlendMode = source.BlendMode, PrecisionChannels = source.PrecisionChannels
            };
            if (decoded.Kind == ArtDocumentSourceKind.Photoshop && !source.IsGroup)
            {
                layer.LocalIdentifier = AllocateId("psd-layer:" + source.StableId, usedIds);
                layer.TextureReference = WriteTexture(context.sourcePath, layer.LocalIdentifier,
                    source.Width, source.Height, source.Rgba).Reference;
            }
            layerIndices[index] = document.Layers.Count;
            document.Layers.Add(layer);
        }
        foreach (var layer in document.Layers)
        {
            var parent = layer.ParentIndex;
            while (parent >= 0 && !layerIndices.ContainsKey(parent)) parent = decoded.Layers[parent].ParentIndex;
            layer.ParentIndex = parent >= 0 ? layerIndices[parent] : -1;
        }
        foreach (var source in decoded.Frames)
        {
            var localId = AllocateId("ase-frame:" + source.Index, usedIds);
            document.Frames.Add(new ArtFrame2D
            {
                Index = source.Index, DurationMilliseconds = source.DurationMilliseconds,
                LocalIdentifier = localId,
                TextureReference = WriteTexture(context.sourcePath, localId,
                    decoded.Width, decoded.Height, source.Rgba).Reference
            });
        }
        document.Tags.AddRange(decoded.Tags.Select(source => new AsepriteTag2D
        {
            Name = source.Name, FromFrame = source.From, ToFrame = source.To,
            Direction = source.Direction, Repeat = source.Repeat
        }));
        document.Validate();
        context.WriteArtifact(Encoding.UTF8.GetBytes(document.Serialize()));
        bool IsVisible(int index)
        {
            for (var current = index; current >= 0; current = decoded.Layers[current].ParentIndex)
                if (!decoded.Layers[current].Visible) return false;
            return true;
        }
    }

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        importHiddenLayers = Get(settings, nameof(importHiddenLayers), true);
        makeTexturesReadable = Get(settings, nameof(makeTexturesReadable), true);
    }

    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        Set(settings, nameof(importHiddenLayers), importHiddenLayers);
        Set(settings, nameof(makeTexturesReadable), makeTexturesReadable);
    }

    private GeneratedAssetArtifact WriteTexture(string ownerPath, long localId, int width, int height, byte[] rgba)
    {
        if (width < 1 || height < 1 || rgba.Length != checked(width * height * 4))
            throw new InvalidDataException("Generated art texture dimensions do not match its pixels.");
        var texture = Texture.FromRgba32(width, height, rgba);
        byte[] png;
        try { png = texture.EncodeToPNG(); }
        finally { BObject.DestroyImmediate(texture); }
        var generated = GeneratedAssetArtifacts.Write(ownerPath, localId, ".png", png);
        var metaPath = generated.ArtifactPath + ".meta";
        var meta = YamlUtility.Load<AssetMetaDocument>(metaPath);
        meta.AssetType = nameof(Texture);
        meta.Importer = nameof(TextureImporter);
        meta.Settings ??= [];
        meta.Settings[nameof(TextureImporter.textureType)] = nameof(TextureImporterType.Texture);
        meta.Settings[nameof(TextureImporter.isReadable)] = makeTexturesReadable.ToString();
        meta.Settings[nameof(TextureImporter.sRGBTexture)] = bool.TrueString;
        meta.Settings[nameof(TextureImporter.filterMode)] = nameof(TextureFilterMode.Point);
        YamlUtility.Save(meta, metaPath);
        return generated;
    }

    private static long AllocateId(string key, ISet<long> used)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var value = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(hash) & long.MaxValue;
        value = Math.Max(3_000_000, value);
        while (!used.Add(value)) value = checked(value + 1);
        return value;
    }
}
