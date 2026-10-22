using System.IO.Compression;
using System.Text;

namespace BEngine.Editor;

internal static class AsepriteDecoder
{
    internal static string[] GetSourceDependencies(string sourcePath)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Read(Path.GetFullPath(sourcePath), 0); dependencies.Remove(Path.GetFullPath(sourcePath)); return dependencies.ToArray();
        void Read(string path, int nesting)
        {
            if (nesting > 16 || dependencies.Count > 1024 || !active.Add(path)) throw new InvalidDataException("Cyclic or excessive external Aseprite dependencies.");
            try
            {
                if (!dependencies.Add(path)) return;
                var length = new FileInfo(path).Length; if (length < 128 || length > 512 * 1024 * 1024) throw new InvalidDataException("Invalid external Aseprite source size.");
                var reader = new LittleEndianReader(File.ReadAllBytes(path)); _ = reader.U32();
                if (reader.U16() != 0xA5E0) throw new InvalidDataException("Invalid Aseprite dependency header.");
                var frames = reader.U16(); reader.Position = 128; var files = new Dictionary<uint, string>(); var used = new HashSet<uint>();
                for (var frame = 0; frame < frames; frame++)
                {
                    var size = checked((int)reader.U32()); var end = reader.CheckedEnd(size - 4, "frame");
                    if (reader.U16() != 0xF1FA) throw new InvalidDataException("Invalid dependency frame.");
                    var oldCount = reader.U16(); reader.Skip(4); var newCount = reader.U32(); var count = oldCount == ushort.MaxValue ? newCount : oldCount;
                    if (count > 1000000) throw new InvalidDataException("Too many dependency chunks.");
                    for (var chunk = 0; chunk < count; chunk++)
                    {
                        var chunkSize = checked((int)reader.U32()); var chunkEnd = reader.CheckedEnd(chunkSize - 4, "chunk");
                        if (chunkEnd > end || chunkSize < 6) throw new InvalidDataException("Invalid dependency chunk.");
                        var type = reader.U16(); var payload = new LittleEndianReader(reader.Slice(chunkEnd - reader.Position));
                        if (type == 0x2008) ReadExternalFiles(ref payload, files);
                        else if (type == 0x2023)
                        {
                            _ = payload.U32(); var flags = payload.U32(); payload.Skip(4 + 2 + 2 + 16); _ = payload.String();
                            if ((flags & 3) == 1) used.Add(payload.U32());
                        }
                    }
                    reader.Position = end;
                }
                foreach (var id in used)
                {
                    if (!files.TryGetValue(id, out var name)) throw new InvalidDataException("Missing external tileset file entry.");
                    Read(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, name)), nesting + 1);
                }
            }
            finally { active.Remove(path); }
        }
    }
    internal static DecodedArtDocument Decode(ReadOnlySpan<byte> bytes, string? sourcePath = null)
    {
        if (bytes.Length < 128 || bytes.Length > 512 * 1024 * 1024)
            throw new InvalidDataException("Aseprite input size is invalid.");
        var reader = new LittleEndianReader(bytes);
        var fileSize = reader.U32();
        if (fileSize > bytes.Length || reader.U16() != 0xA5E0) throw new InvalidDataException("Aseprite header is invalid.");
        var frameCount = reader.U16(); var width = reader.U16(); var height = reader.U16(); var depth = reader.U16();
        var headerFlags = reader.U32(); var legacySpeed = reader.U16(); reader.Skip(8); var transparentIndex = reader.Byte(); reader.Skip(3);
        reader.Skip(2); reader.Skip(1); reader.Skip(1); reader.Skip(2); reader.Skip(2); reader.Skip(2); reader.Skip(2); reader.Skip(84);
        if (frameCount is < 1 or > 4096 || width is < 1 or > 16384 || height is < 1 or > 16384 || depth is not (8 or 16 or 32))
            throw new NotSupportedException("Aseprite import supports 8-bit indexed, 16-bit grayscale/alpha, and 32-bit RGBA documents up to 16384 pixels.");
        var layers = new List<AseLayer>();
        var frameCels = new List<Dictionary<int, AseCel>>(frameCount);
        var framePalettes = new List<AseColor[]>(frameCount);
        var palette = CreateDefaultPalette();
        var modernPalette = false;
        var tilesets = new List<AseTileset>();
        var externalFiles = new Dictionary<uint, string>();
        var durations = new List<int>(frameCount);
        var tags = new List<DecodedAsepriteTag>();
        var slices = new List<AsepriteSlice2D>(); var metadata = new List<ArtSourceMetadata>();
        var scope = "Sprite"; var tagDataIndex = -1; var tileDataIndex = -1; var tilesetScope = ""; var metadataBytes = 0L;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frameStart = reader.Position;
            var frameBytes = checked((int)reader.U32());
            var frameEnd = reader.CheckedEnd(frameBytes - 4, "frame");
            if (reader.U16() != 0xF1FA) throw new InvalidDataException($"Aseprite frame {frameIndex} magic is invalid.");
            var oldChunkCount = reader.U16();
            var duration = reader.U16(); reader.Skip(2); var newChunkCount = reader.U32();
            var chunkCount = oldChunkCount == ushort.MaxValue ? newChunkCount : oldChunkCount;
            if (chunkCount > 1_000_000) throw new InvalidDataException("Aseprite frame has too many chunks.");
            durations.Add(Math.Max(1, (int)(duration == 0 ? legacySpeed : duration)));
            var cels = new Dictionary<int, AseCel>();
            for (var chunkIndex = 0u; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunkStart = reader.Position;
                var chunkSize = checked((int)reader.U32());
                var chunkEnd = reader.CheckedEnd(chunkSize - 4, "chunk");
                if (chunkSize < 6 || chunkEnd > frameEnd) throw new InvalidDataException("Aseprite chunk exceeds its frame.");
                var type = reader.U16();
                var raw = reader.Slice(chunkEnd - reader.Position);
                var payload = new LittleEndianReader(raw);
                if (type == 0x2004) { scope = "Layer:" + layers.Count; tagDataIndex = tileDataIndex = -1; }
                else if (type == 0x2005) { scope = "Cel:" + payload.U16(); payload.Position = 0; tagDataIndex = tileDataIndex = -1; }
                else if (type == 0x2018) { tagDataIndex = tags.Count; scope = "Tags"; tileDataIndex = -1; }
                else if (type == 0x2023) { scope = tilesetScope = "Tileset:" + tilesets.Count; tileDataIndex = -1; tagDataIndex = -1; }
                else if (type == 0x2022) { scope = "Slice:" + slices.Count; tagDataIndex = tileDataIndex = -1; }
                else if (type == 0x2019 && frameIndex == 0) { scope = "Sprite"; tagDataIndex = tileDataIndex = -1; }
                if (type == 0x2020 && tagDataIndex >= 0) scope = "Tag:" + tagDataIndex++;
                else if (type == 0x2020 && scope.StartsWith("Tileset:", StringComparison.Ordinal))
                { scope = tileDataIndex >= 0 ? tilesetScope + ":Tile:" + tileDataIndex : tilesetScope; tileDataIndex++; }
                if (type is not (0x2005 or 0x2023 or 0x0004 or 0x0011 or 0x2019))
                {
                    metadataBytes += raw.Length;
                    if (metadataBytes > 32 * 1024 * 1024) throw new InvalidDataException("Aseprite metadata exceeds 32 MiB.");
                    metadata.Add(new() { Scope = scope, Frame = frameIndex, Type = type, DataBase64 = Convert.ToBase64String(raw) });
                }
                if (type == 0x2004) ReadLayer(ref payload, layers, headerFlags);
                else if (type == 0x2005) ReadCel(ref payload, cels, depth, frameIndex, payload.Remaining);
                else if (type == 0x2018) ReadTags(ref payload, tags);
                else if (type == 0x2019) { ReadPalette(ref payload, palette); modernPalette = true; }
                else if (type is 0x0004 or 0x0011 && !modernPalette) ReadLegacyPalette(ref payload, palette, type == 0x0011);
                else if (type == 0x2023) ReadTileset(ref payload, tilesets, depth);
                else if (type == 0x2008) ReadExternalFiles(ref payload, externalFiles);
                else if (type == 0x2022) ReadSlice(ref payload, slices, frameCount);
                if (reader.Position <= chunkStart) throw new InvalidDataException("Aseprite chunk did not advance.");
            }
            reader.Position = frameEnd;
            frameCels.Add(cels);
            framePalettes.Add((AseColor[])palette.Clone());
            if (reader.Position <= frameStart) throw new InvalidDataException("Aseprite frame did not advance.");
        }
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ResolveExternalTilesets(tilesets, externalFiles, sourcePath, dependencies, [], 0);
        var result = new DecodedArtDocument { Kind = ArtDocumentSourceKind.Aseprite, Width = width, Height = height, SourceBitDepth = depth };
        result.SourceDependencies.AddRange(dependencies);
        result.Metadata.AddRange(metadata); result.Slices.AddRange(slices);
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            result.Layers.Add(new DecodedArtLayer
            {
                StableId = layer.StableId, Name = layer.Name, X = 0, Y = 0, Width = width, Height = height,
                Opacity = layer.Opacity, Visible = layer.Visible, Rgba = [], ParentIndex = layer.Parent, IsGroup = layer.Type == 1,
                BlendMode = layer.BlendMode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        for (var frame = 0; frame < frameCount; frame++)
            result.Frames.Add(new DecodedArtFrame
            {
                Index = frame, DurationMilliseconds = durations[frame],
                Rgba = CompositeFrame(frame, width, height, layers, frameCels, framePalettes[frame], transparentIndex, tilesets, (headerFlags & 2) != 0)
            });
        result.Tags.AddRange(tags);
        foreach (var tag in result.Tags)
            if (tag.From < 0 || tag.To < tag.From || tag.To >= frameCount)
                throw new InvalidDataException($"Aseprite tag '{tag.Name}' has an invalid frame range.");
        return result;
    }

    private static void ReadSlice(ref LittleEndianReader reader, List<AsepriteSlice2D> slices, int frames)
    {
        var count = reader.U32(); var flags = reader.U32(); reader.Skip(4); var name = reader.String();
        if (count > 4096 || name.Length == 0) throw new InvalidDataException("Aseprite slice keys or name are invalid.");
        var slice = new AsepriteSlice2D { Name = name };
        for (var i = 0u; i < count; i++)
        {
            var frame = checked((int)reader.U32());
            if (frame >= frames || i > 0 && frame <= slice.Keys[^1].Frame) throw new InvalidDataException("Aseprite slice frame keys are invalid.");
            var key = new AsepriteSliceKey2D { Frame = frame, Bounds = new(reader.I32(), reader.I32(), reader.U32(), reader.U32()) };
            if ((flags & 1) != 0) key.Center = new(reader.I32(), reader.I32(), reader.U32(), reader.U32());
            if ((flags & 2) != 0) key.Pivot = new(reader.I32(), reader.I32());
            slice.Keys.Add(key);
        }
        slices.Add(slice);
    }

    private static void ReadLayer(ref LittleEndianReader reader, List<AseLayer> layers, uint headerFlags)
    {
        var flags = reader.U16(); var type = reader.U16(); var level = reader.U16(); reader.Skip(4);
        var blend = reader.U16(); var opacity = reader.Byte(); reader.Skip(3); var name = reader.String();
        if (type > 2 || blend > 18 || level > 128) throw new NotSupportedException("Aseprite layer type, blend mode or hierarchy depth is unsupported.");
        var tilesetIndex = type == 2 ? checked((int)reader.U32()) : -1;
        var parent = -1;
        if (level > 0)
        {
            for (var index = layers.Count - 1; index >= 0; index--)
                if (layers[index].Level < level) { parent = index; break; }
            if (parent < 0 || layers[parent].Type != 1 || layers[parent].Level + 1 != level)
                throw new InvalidDataException("Aseprite layer hierarchy is invalid.");
        }
        if ((headerFlags & 1) == 0) opacity = 255;
        var identity = (headerFlags & 4) != 0 ? Convert.ToHexString(reader.Slice(16)) : $"layer:{layers.Count}:{level}:{name}";
        var stableId = unchecked((int)StableHash(identity));
        layers.Add(new AseLayer(stableId, string.IsNullOrWhiteSpace(name) ? $"Layer {layers.Count + 1}" : name.Trim(),
            (flags & 1) != 0, opacity, type, blend, level, parent, (flags & 8) != 0, tilesetIndex));
    }

    private static void ReadCel(ref LittleEndianReader reader, IDictionary<int, AseCel> cels, int depth, int frameIndex, int chunkEnd)
    {
        var layer = reader.U16(); var x = reader.I16(); var y = reader.I16(); var opacity = reader.Byte();
        var type = reader.U16(); var zIndex = reader.I16(); reader.Skip(5);
        if (type == 1)
        {
            cels[layer] = new AseCel(x, y, opacity, 0, 0, [], reader.U16()) { ZIndex = zIndex };
            return;
        }
        if (type is not (0 or 2 or 3)) throw new NotSupportedException($"Aseprite cel type {type} is unsupported.");
        var width = reader.U16(); var height = reader.U16();
        if (type == 3)
        {
            var bits = reader.U16(); var idMask = reader.U32(); var xFlip = reader.U32(); var yFlip = reader.U32(); var diagonal = reader.U32();
            reader.Skip(10);
            if (bits is not (8 or 16 or 32) || idMask == 0) throw new InvalidDataException("Aseprite tile ID format is invalid.");
            var tiles = Inflate(reader.Slice(reader.Remaining), checked(width * height * (bits / 8)));
            cels[layer] = new AseCel(x, y, opacity, width, height, tiles, depth, -1)
            { ZIndex = zIndex, Tilemap = new TilemapFormat(bits, idMask, xFlip, yFlip, diagonal) };
            return;
        }
        var bytesPerPixel = depth / 8;
        var expected = checked(width * height * bytesPerPixel);
        byte[] pixels;
        if (type == 0) pixels = reader.Slice(expected).ToArray();
        else
        {
            pixels = Inflate(reader.Slice(chunkEnd - reader.Position), expected);
        }
        cels[layer] = new AseCel(x, y, opacity, width, height, pixels, depth, -1) { ZIndex = zIndex };
    }

    private static byte[] Inflate(ReadOnlySpan<byte> encoded, int expected)
    {
        if (expected < 0 || expected > 256 * 1024 * 1024) throw new InvalidDataException("Aseprite decoded pixels exceed the import budget.");
        using var input = new MemoryStream(encoded.ToArray(), false);
        using var zip = new ZLibStream(input, CompressionMode.Decompress);
        var pixels = new byte[expected]; zip.ReadExactly(pixels);
        if (zip.ReadByte() >= 0) throw new InvalidDataException("Aseprite image decoded excess bytes.");
        return pixels;
    }

    private static void ReadTileset(ref LittleEndianReader reader, ICollection<AseTileset> tilesets, int depth)
    {
        var id = reader.U32(); var flags = reader.U32(); var count = checked((int)reader.U32());
        var width = reader.U16(); var height = reader.U16(); reader.Skip(16); _ = reader.String();
        uint? externalFile = null; uint externalId = 0;
        if ((flags & 1) != 0) { externalFile = reader.U32(); externalId = reader.U32(); }
        byte[] pixels = [];
        if ((flags & 2) != 0)
        {
            if (count <= 0 || width == 0 || height == 0) throw new InvalidDataException("Aseprite tileset bounds are invalid.");
            var length = checked((int)reader.U32());
            pixels = Inflate(reader.Slice(length), checked(width * height * count * (depth / 8)));
        }
        tilesets.Add(new AseTileset(id, width, height, count, depth, (flags & 4) != 0, pixels, externalFile, externalId));
    }

    private static void ReadExternalFiles(ref LittleEndianReader reader, IDictionary<uint, string> files)
    {
        var count = reader.U32(); reader.Skip(8);
        if (count > 65536) throw new InvalidDataException("Aseprite external file table exceeds its limit.");
        for (var index = 0; index < count; index++)
        {
            var id = reader.U32(); var type = reader.Byte(); reader.Skip(7); var name = reader.String();
            if (type == 1 && !files.TryAdd(id, name)) throw new InvalidDataException("Duplicate Aseprite external tileset file ID.");
        }
    }
    private static void ResolveExternalTilesets(List<AseTileset> tilesets, Dictionary<uint, string> files, string? sourcePath,
        HashSet<string> dependencies, HashSet<string> active, int nesting)
    {
        if (nesting > 16) throw new InvalidDataException("Aseprite external tilesets exceed 16 nested files.");
        for (var index = 0; index < tilesets.Count; index++)
        {
            var tile = tilesets[index]; if (tile.Pixels.Length > 0 || tile.ExternalFile is not { } file) continue;
            if (sourcePath is null || !files.TryGetValue(file, out var name)) throw new InvalidDataException("External tileset requires its source path and external file entry.");
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, name));
            if (!active.Add(path)) throw new InvalidDataException("Cyclic Aseprite external tileset reference: " + path);
            try
            {
                dependencies.Add(path);
                var sourceLength = new FileInfo(path).Length;
                if (sourceLength < 128 || sourceLength > 512 * 1024 * 1024) throw new InvalidDataException("External Aseprite tileset size is invalid.");
                var bytes = File.ReadAllBytes(path);
                var reader = new LittleEndianReader(bytes); _ = reader.U32();
                if (reader.U16() != 0xA5E0) throw new InvalidDataException("External tileset is not an Aseprite file.");
                var frames = reader.U16(); reader.Skip(4); var depth = reader.U16(); reader.Position = 128;
                if (depth != tile.Depth) throw new NotSupportedException("External tileset and owner must have matching color depths.");
                var external = new List<AseTileset>(); var externalFiles = new Dictionary<uint, string>();
                for (var frame = 0; frame < frames; frame++)
                {
                    var length = checked((int)reader.U32()); var frameEnd = reader.CheckedEnd(length - 4, "external frame");
                    if (reader.U16() != 0xF1FA) throw new InvalidDataException("Invalid external tileset frame.");
                    var oldCount = reader.U16(); reader.Skip(4); var newCount = reader.U32(); var count = oldCount == ushort.MaxValue ? newCount : oldCount;
                    if (count > 1000000) throw new InvalidDataException("Too many external tileset chunks.");
                    for (var chunk = 0; chunk < count; chunk++)
                    {
                        var size = checked((int)reader.U32()); var end = reader.CheckedEnd(size - 4, "external chunk");
                        if (end > frameEnd || size < 6) throw new InvalidDataException("Invalid external tileset chunk.");
                        var type = reader.U16(); var payload = new LittleEndianReader(reader.Slice(end - reader.Position));
                        if (type == 0x2023) ReadTileset(ref payload, external, depth);
                        else if (type == 0x2008) ReadExternalFiles(ref payload, externalFiles);
                    }
                    reader.Position = frameEnd;
                }
                ResolveExternalTilesets(external, externalFiles, path, dependencies, active, nesting + 1);
                var found = external.SingleOrDefault(item => item.Id == tile.ExternalId) ?? throw new InvalidDataException("External Aseprite tileset ID is missing.");
                if (found.Width != tile.Width || found.Height != tile.Height || found.Count != tile.Count) throw new InvalidDataException("External tileset dimensions/count do not match its reference.");
                tilesets[index] = tile with { Pixels = found.Pixels };
            }
            finally { active.Remove(path); }
        }
    }

    private static void ReadLegacyPalette(ref LittleEndianReader reader, AseColor[] palette, bool sixBit)
    {
        var packets = reader.U16(); var index = 0;
        for (var packet = 0; packet < packets; packet++)
        {
            index += reader.Byte(); var size = (int)reader.Byte(); if (size == 0) size = 256;
            if (index + size > palette.Length) throw new InvalidDataException("Aseprite legacy palette range is invalid.");
            for (var color = 0; color < size; color++)
            {
                var r = reader.Byte(); var g = reader.Byte(); var b = reader.Byte();
                if (sixBit) { r = (byte)Math.Min(255, r * 255 / 63); g = (byte)Math.Min(255, g * 255 / 63); b = (byte)Math.Min(255, b * 255 / 63); }
                palette[index++] = new AseColor(r, g, b, 255);
            }
        }
    }

    private static void ReadPalette(ref LittleEndianReader reader, AseColor[] palette)
    {
        var declaredSize = reader.U32();
        var first = reader.U32();
        var last = reader.U32();
        reader.Skip(8);
        if (declaredSize == 0 || declaredSize > 65_536 || first > last || last >= declaredSize || last >= palette.Length)
            throw new InvalidDataException("Aseprite palette range is invalid.");
        for (var index = first; index <= last; index++)
        {
            var flags = reader.U16();
            palette[index] = new AseColor(reader.Byte(), reader.Byte(), reader.Byte(), reader.Byte());
            if ((flags & 1) != 0) _ = reader.String();
        }
    }

    private static void ReadTags(ref LittleEndianReader reader, ICollection<DecodedAsepriteTag> tags)
    {
        var count = reader.U16(); reader.Skip(8);
        for (var index = 0; index < count; index++)
        {
            var from = reader.U16(); var to = reader.U16(); var directionValue = reader.Byte(); var repeat = reader.U16();
            reader.Skip(6); reader.Skip(4); var name = reader.String();
            var direction = directionValue switch
            {
                0 => AsepriteTagDirection.Forward,
                1 => AsepriteTagDirection.Reverse,
                2 => AsepriteTagDirection.PingPong,
                3 => AsepriteTagDirection.PingPongReverse,
                _ => throw new InvalidDataException($"Aseprite tag direction {directionValue} is invalid.")
            };
            tags.Add(new DecodedAsepriteTag
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"Tag {tags.Count + 1}" : name.Trim(),
                From = from, To = to, Direction = direction, Repeat = repeat
            });
        }
    }

    private static byte[] CompositeFrame(int frame, int width, int height,
        IReadOnlyList<AseLayer> layers, IReadOnlyList<Dictionary<int, AseCel>> frames,
        IReadOnlyList<AseColor> palette, byte transparentIndex, IReadOnlyList<AseTileset> tilesets, bool isolatedGroups)
    {
        var output = new byte[checked(width * height * 4)];
        DrawChildren(output, -1);
        return output;

        void DrawChildren(byte[] targetPixels, int parent)
        {
            var children = Enumerable.Range(0, layers.Count).Where(index => layers[index].Parent == parent)
                .OrderBy(index => index + (frames[frame].TryGetValue(index, out var cel) ? cel.ZIndex : 0))
                .ThenBy(index => frames[frame].TryGetValue(index, out var cel) ? cel.ZIndex : 0);
            foreach (var layerIndex in children)
            {
                var layer = layers[layerIndex];
                if (!layer.Visible) continue;
                if (layer.Type == 1)
                {
                    if (!isolatedGroups) DrawChildren(targetPixels, layerIndex);
                    else
                    {
                        var group = new byte[targetPixels.Length]; DrawChildren(group, layerIndex);
                        for (var pixel = 0; pixel < width * height; pixel++)
                        {
                            var offset = pixel * 4;
                            CompositePixel(targetPixels.AsSpan(offset, 4), new AseColor(group[offset], group[offset + 1], group[offset + 2], group[offset + 3]), layer.Opacity * 255, layer.BlendMode);
                        }
                    }
                    continue;
                }
                if (!TryResolveCel(frame, layerIndex, frames, out var cel)) continue;
                if (cel.Tilemap is not null) cel = ExpandTilemap(cel, layer, tilesets);
                var opacity = layer.Opacity * cel.Opacity;
                for (var y = 0; y < cel.Height; y++)
                for (var x = 0; x < cel.Width; x++)
                {
                    var targetX = cel.X + x; var targetY = cel.Y + y;
                    if ((uint)targetX >= (uint)width || (uint)targetY >= (uint)height) continue;
                    var pixel = y * cel.Width + x;
                    var source = ReadPixel(cel, pixel, palette, transparentIndex, layer.Background);
                    var target = (targetY * width + targetX) * 4;
                    CompositePixel(targetPixels.AsSpan(target, 4), source, opacity, layer.BlendMode);
                }
            }
        }
    }

    private static AseCel ExpandTilemap(AseCel cel, AseLayer layer, IReadOnlyList<AseTileset> tilesets)
    {
        if ((uint)layer.TilesetIndex >= (uint)tilesets.Count || tilesets[layer.TilesetIndex].Pixels.Length == 0)
            throw new InvalidDataException("Aseprite tilemap has no resolved tileset pixels.");
        var tileset = tilesets[layer.TilesetIndex]; var format = cel.Tilemap!;
        var width = checked(cel.Width * tileset.Width); var height = checked(cel.Height * tileset.Height);
        var size = checked(width * height * (tileset.Depth / 8));
        if (size > 256 * 1024 * 1024) throw new InvalidDataException("Aseprite expanded tilemap exceeds the import budget.");
        var pixels = new byte[size]; var bpp = tileset.Depth / 8;
        for (var ty = 0; ty < cel.Height; ty++)
        for (var tx = 0; tx < cel.Width; tx++)
        {
            var tileOffset = (ty * cel.Width + tx) * (format.Bits / 8);
            var data = cel.Pixels.AsSpan(tileOffset);
            uint value = format.Bits == 8 ? data[0] : format.Bits == 16
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data)
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data);
            var tile = value & format.IdMask;
            if (tileset.ZeroIsEmpty ? tile == 0 : value == uint.MaxValue) continue;
            if (tile >= tileset.Count) throw new InvalidDataException("Aseprite tile references a nonexistent tileset entry.");
            if ((value & format.Diagonal) != 0 && tileset.Width != tileset.Height)
                throw new NotSupportedException("Aseprite diagonal flips require square tiles.");
            for (var y = 0; y < tileset.Height; y++)
            for (var x = 0; x < tileset.Width; x++)
            {
                var sx = (value & format.XFlip) != 0 ? tileset.Width - 1 - x : x;
                var sy = (value & format.YFlip) != 0 ? tileset.Height - 1 - y : y;
                if ((value & format.Diagonal) != 0) (sx, sy) = (sy, sx);
                var source = checked(((int)tile * tileset.Width * tileset.Height + sy * tileset.Width + sx) * bpp);
                var target = ((ty * tileset.Height + y) * width + tx * tileset.Width + x) * bpp;
                tileset.Pixels.AsSpan(source, bpp).CopyTo(pixels.AsSpan(target, bpp));
            }
        }
        return cel with { Width = width, Height = height, Pixels = pixels, Depth = tileset.Depth, Tilemap = null };
    }

    private static bool TryResolveCel(int frame, int layer, IReadOnlyList<Dictionary<int, AseCel>> frames, out AseCel cel)
    {
        var guard = frames.Count;
        AseCel? placement = null;
        while (guard-- > 0 && frame >= 0 && frame < frames.Count && frames[frame].TryGetValue(layer, out cel))
        {
            placement ??= cel;
            if (cel.LinkedFrame < 0)
            {
                cel = cel with { X = placement.Value.X, Y = placement.Value.Y, Opacity = placement.Value.Opacity, ZIndex = placement.Value.ZIndex };
                return true;
            }
            frame = cel.LinkedFrame;
        }
        cel = default;
        return false;
    }

    private static AseColor ReadPixel(in AseCel cel, int pixel, IReadOnlyList<AseColor> palette, byte transparentIndex, bool background)
    {
        if (cel.Depth == 32)
        {
            var offset = pixel * 4;
            return new AseColor(cel.Pixels[offset], cel.Pixels[offset + 1], cel.Pixels[offset + 2], cel.Pixels[offset + 3]);
        }
        if (cel.Depth == 16)
        {
            var offset = pixel * 2;
            return new AseColor(cel.Pixels[offset], cel.Pixels[offset], cel.Pixels[offset], cel.Pixels[offset + 1]);
        }
        var index = cel.Pixels[pixel];
        var color = palette[index];
        return !background && index == transparentIndex ? color with { A = 0 } : color;
    }

    internal static void CompositeRgba(Span<byte> target, ReadOnlySpan<byte> source, byte opacity, ushort blendMode) =>
        CompositePixel(target, new AseColor(source[0], source[1], source[2], source[3]), opacity * 255, blendMode);

    private static void CompositePixel(Span<byte> target, AseColor source, int opacity, ushort blendMode)
    {
        var sourceAlpha = source.A / 255d * opacity / (255d * 255d);
        if (sourceAlpha <= 0) return;
        var destinationAlpha = target[3] / 255d;
        var combinedAlpha = sourceAlpha + destinationAlpha * (1d - sourceAlpha);
        var componentBlend = blendMode is >= 12 and <= 15 ? BlendComponents(target, source, blendMode) : default;
        for (var channel = 0; channel < 3; channel++)
        {
            var sourceColor = source[channel] / 255d;
            var destinationColor = target[channel] / 255d;
            var blend = blendMode is >= 12 and <= 15 ? componentBlend[channel] : BlendChannel(destinationColor, sourceColor, blendMode);
            var premultiplied = sourceAlpha * (1d - destinationAlpha) * sourceColor +
                                sourceAlpha * destinationAlpha * blend +
                                (1d - sourceAlpha) * destinationAlpha * destinationColor;
            target[channel] = ToByte(premultiplied / combinedAlpha);
        }
        target[3] = ToByte(combinedAlpha);
    }

    private static double BlendChannel(double backdrop, double source, ushort mode) => mode switch
    {
        0 => source,
        1 => backdrop * source,
        2 => backdrop + source - backdrop * source,
        3 => backdrop <= 0.5 ? 2 * backdrop * source : 1 - 2 * (1 - backdrop) * (1 - source),
        4 => Math.Min(backdrop, source),
        5 => Math.Max(backdrop, source),
        6 => source >= 1 ? 1 : Math.Min(1, backdrop / (1 - source)),
        7 => source <= 0 ? 0 : 1 - Math.Min(1, (1 - backdrop) / source),
        8 => source <= 0.5 ? 2 * backdrop * source : 1 - 2 * (1 - backdrop) * (1 - source),
        9 => source <= 0.5
            ? backdrop - (1 - 2 * source) * backdrop * (1 - backdrop)
            : backdrop + (2 * source - 1) * (SoftLightD(backdrop) - backdrop),
        10 => Math.Abs(backdrop - source),
        11 => backdrop + source - 2 * backdrop * source,
        16 => Math.Min(1, backdrop + source),
        17 => Math.Max(0, backdrop - source),
        18 => source <= 0 ? 1 : Math.Min(1, backdrop / source),
        _ => source
    };

    private static double SoftLightD(double value) => value <= 0.25
        ? ((16 * value - 12) * value + 4) * value
        : Math.Sqrt(value);

    private static ColorVector BlendComponents(ReadOnlySpan<byte> backdrop, AseColor source, ushort mode)
    {
        var b = new ColorVector(backdrop[0] / 255d, backdrop[1] / 255d, backdrop[2] / 255d);
        var s = new ColorVector(source.R / 255d, source.G / 255d, source.B / 255d);
        return mode switch
        {
            12 => SetLum(SetSat(s, b.Saturation), b.Luminance),
            13 => SetLum(SetSat(b, s.Saturation), b.Luminance),
            14 => SetLum(s, b.Luminance),
            _ => SetLum(b, s.Luminance)
        };
    }

    private static ColorVector SetSat(ColorVector color, double saturation)
    {
        var minimum = color.Minimum; var range = color.Maximum - minimum;
        return range > 0 ? new((color.R - minimum) * saturation / range, (color.G - minimum) * saturation / range, (color.B - minimum) * saturation / range) : default;
    }

    private static ColorVector SetLum(ColorVector color, double luminance)
    {
        var delta = luminance - color.Luminance;
        color = new(color.R + delta, color.G + delta, color.B + delta);
        var minimum = color.Minimum; var maximum = color.Maximum;
        if (minimum < 0) color = new(luminance + (color.R - luminance) * luminance / (luminance - minimum), luminance + (color.G - luminance) * luminance / (luminance - minimum), luminance + (color.B - luminance) * luminance / (luminance - minimum));
        if (maximum > 1) color = new(luminance + (color.R - luminance) * (1 - luminance) / (maximum - luminance), luminance + (color.G - luminance) * (1 - luminance) / (maximum - luminance), luminance + (color.B - luminance) * (1 - luminance) / (maximum - luminance));
        return color;
    }

    private readonly record struct ColorVector(double R, double G, double B)
    {
        internal double this[int index] => index == 0 ? R : index == 1 ? G : B;
        internal double Minimum => Math.Min(R, Math.Min(G, B));
        internal double Maximum => Math.Max(R, Math.Max(G, B));
        internal double Saturation => Maximum - Minimum;
        internal double Luminance => .3 * R + .59 * G + .11 * B;
    }
    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static AseColor[] CreateDefaultPalette()
    {
        var result = new AseColor[256];
        for (var index = 0; index < result.Length; index++)
            result[index] = new AseColor((byte)index, (byte)index, (byte)index, 255);
        return result;
    }

    private static uint StableHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private readonly record struct AseLayer(int StableId, string Name, bool Visible, byte Opacity, ushort Type, ushort BlendMode,
        int Level, int Parent, bool Background, int TilesetIndex);
    private sealed record AseTileset(uint Id, int Width, int Height, int Count, int Depth, bool ZeroIsEmpty, byte[] Pixels, uint? ExternalFile, uint ExternalId);
    private sealed record TilemapFormat(int Bits, uint IdMask, uint XFlip, uint YFlip, uint Diagonal);
    private readonly record struct AseCel(int X, int Y, byte Opacity, int Width, int Height, byte[] Pixels, int Depth, int LinkedFrame)
    {
        internal int ZIndex { get; init; }
        internal TilemapFormat? Tilemap { get; init; }
        internal AseCel(int x, int y, byte opacity, int width, int height, byte[] pixels, int linkedFrame)
            : this(x, y, opacity, width, height, pixels, 0, linkedFrame) { }
    }
    private readonly record struct AseColor(byte R, byte G, byte B, byte A)
    {
        internal byte this[int index] => index switch { 0 => R, 1 => G, 2 => B, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    }

    private ref struct LittleEndianReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        internal int Position { get; set; }
        internal int Remaining => _bytes.Length - Position;
        internal byte Byte() { Require(1); return _bytes[Position++]; }
        internal ushort U16() { Require(2); var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_bytes[Position..]); Position += 2; return value; }
        internal short I16() => unchecked((short)U16());
        internal int I32() => unchecked((int)U32());
        internal uint U32() { Require(4); var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_bytes[Position..]); Position += 4; return value; }
        internal ReadOnlySpan<byte> Slice(int count) { Require(count); var value = _bytes.Slice(Position, count); Position += count; return value; }
        internal string String() { var length = U16(); return Encoding.UTF8.GetString(Slice(length)); }
        internal void Skip(int count) { Require(count); Position += count; }
        internal int CheckedEnd(int remainingLength, string name) => remainingLength >= 0 && remainingLength <= Remaining
            ? Position + remainingLength : throw new InvalidDataException($"Aseprite {name} length is invalid.");
        private void Require(int count) { if (count < 0 || count > Remaining) throw new InvalidDataException("Aseprite data is truncated."); }
    }
}
