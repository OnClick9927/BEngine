using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BEngine.Editor;

internal static class PhotoshopDecoder
{
    internal static DecodedArtDocument Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30 || bytes.Length > 512 * 1024 * 1024)
            throw new InvalidDataException("PSD/PSB input size is invalid.");
        var reader = new BigEndianReader(bytes);
        if (reader.Ascii(4) != "8BPS") throw new InvalidDataException("Photoshop signature is missing.");
        var version = reader.U16();
        if (version is not (1 or 2)) throw new InvalidDataException($"Unsupported Photoshop version {version}.");
        var psb = version == 2;
        reader.Skip(6);
        var channels = reader.U16();
        var height = checked((int)reader.U32());
        var width = checked((int)reader.U32());
        var depth = reader.U16();
        var colorMode = reader.U16();
        if (channels is < 1 or > 56 || width is < 1 or > 16384 || height is < 1 or > 16384)
            throw new InvalidDataException("Photoshop dimensions or channel count are invalid.");
        if (depth is not (8 or 16 or 32) || colorMode is not (1 or 3 or 4))
            throw new NotSupportedException("PSD/PSB import supports 8/16/32-bit grayscale, RGB and CMYK documents.");
        reader.SkipLength32("color mode data");
        var resourceBytes = reader.Slice(reader.CheckedLength(reader.Length32("image resources"), "image resources"));
        var layerMaskLength = psb ? reader.Length64("layer and mask data") : reader.Length32("layer and mask data");
        var result = new DecodedArtDocument { Kind = ArtDocumentSourceKind.Photoshop, Width = width, Height = height, SourceBitDepth = depth };
        ReadResources(resourceBytes, result);
        if (layerMaskLength == 0) return result;
        var section = new BigEndianReader(reader.Slice(reader.CheckedLength(layerMaskLength, "layer and mask data")));
        var layerInfoLength = psb ? section.Length64("layer info") : section.Length32("layer info");
        var ordinaryInfo = section.Slice(section.CheckedLength(layerInfoLength, "layer info"));
        ReadOnlySpan<byte> selectedInfo = ordinaryInfo;
        // Photoshop commonly stores high-depth layers in Lr16/Lr32 instead of the ordinary layer-info field.
        if (section.Remaining >= 4)
        {
            section.SkipLength32("global layer mask");
            while (section.Remaining >= 12)
            {
                var signature = section.Ascii(4);
                if (signature is not ("8BIM" or "8B64")) throw new InvalidDataException("PSD/PSB global tagged block signature is invalid.");
                var key = section.Ascii(4);
                var length = ReadTaggedLength(ref section, signature, key, psb);
                var data = section.Slice(section.CheckedLength(length, "global tagged block"));
                if (key is not ("Lr16" or "Lr32" or "Layr")) result.Metadata.Add(new() { Scope = "Photoshop.Global:" + key, DataBase64 = Convert.ToBase64String(data) });
                if (key == (depth == 16 ? "Lr16" : depth == 32 ? "Lr32" : "Layr")) selectedInfo = data;
                if ((length & 1) != 0 && section.Remaining > 0) section.Skip(1);
            }
        }
        if (!selectedInfo.IsEmpty) DecodeLayers(selectedInfo, psb, depth, colorMode, result);
        return result;
    }

    private static void ReadResources(ReadOnlySpan<byte> bytes, DecodedArtDocument result)
    {
        var reader = new BigEndianReader(bytes);
        while (reader.Remaining > 0)
        {
            if (reader.Ascii(4) != "8BIM") throw new InvalidDataException("Photoshop image resource signature is invalid.");
            var id = reader.U16(); var length = reader.Byte(); reader.Skip(length);
            if (((length + 1) & 1) != 0) reader.Skip(1);
            var size = reader.CheckedLength(reader.Length32("image resource"), "image resource"); var data = reader.Slice(size);
            result.Metadata.Add(new() { Scope = "Photoshop.ImageResource", Type = id, DataBase64 = Convert.ToBase64String(data) });
            if ((size & 1) != 0) reader.Skip(1);
        }
    }

    private static void DecodeLayers(ReadOnlySpan<byte> data, bool psb, int depth, int colorMode, DecodedArtDocument result)
    {
        var reader = new BigEndianReader(data);
        var count = Math.Abs((int)reader.I16());
        if (count > 4096) throw new InvalidDataException("PSD/PSB layer count exceeds 4096.");
        var records = new List<LayerRecord>(count);
        for (var index = 0; index < count; index++) records.Add(ReadLayerRecord(ref reader, psb, index, result));
        var parents = new int[count]; Array.Fill(parents, -1);
        var groups = new Stack<int>();
        for (var index = count - 1; index >= 0; index--)
        {
            var record = records[index];
            if (record.SectionType == 3)
            {
                if (groups.Count == 0) throw new InvalidDataException("PSD group divider is unbalanced.");
                groups.Pop(); continue;
            }
            parents[index] = groups.TryPeek(out var parent) ? parent : -1;
            if (record.SectionType is 1 or 2) groups.Push(index);
        }
        if (groups.Count != 0) throw new InvalidDataException("PSD group folder is unbalanced.");
        var outputIndices = new Dictionary<int, int>();
        using var colors = PhotoshopColorTransform.Open(result, colorMode);
        long decodedBytes = 0;
        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var record = records[recordIndex];
            var decodedChannels = new Dictionary<short, byte[]>();
            var precision = new List<ArtPrecisionChannel>();
            foreach (var channel in record.Channels)
            {
                var block = reader.Slice(reader.CheckedLength(channel.Length, "layer channel"));
                var mask = channel.Id == -3 ? record.RealMask : channel.Id == -2 ? record.Mask : null;
                if (channel.Id is -2 or -3 && mask is null) continue;
                var channelWidth = mask?.Width ?? record.Width;
                var channelHeight = mask?.Height ?? record.Height;
                decodedBytes += (long)channelWidth * channelHeight * (depth / 8 + 1);
                if (decodedBytes > 512 * 1024 * 1024) throw new InvalidDataException("PSD/PSB decoded layers exceed the 512 MiB import budget.");
                decodedChannels[channel.Id] = DecodeChannel(block, channelWidth, channelHeight, psb, depth, out var raw);
                if (depth > 8) precision.Add(new() { Id = channel.Id, Width = channelWidth, Height = channelHeight, BitDepth = depth, SamplesBase64 = Convert.ToBase64String(raw) });
            }
            if (record.SectionType == 3) continue;
            if (record.SectionType is 1 or 2)
            {
                outputIndices.Add(recordIndex, result.Layers.Count);
                result.Layers.Add(new() { StableId = record.StableId, Name = record.Name, X = record.Left, Y = record.Top,
                    Width = record.Width, Height = record.Height, Opacity = record.Opacity, Visible = record.Visible,
                    IsGroup = true, Rgba = [], BlendMode = record.BlendMode });
                continue;
            }
            if (record.Width == 0 || record.Height == 0 || !decodedChannels.ContainsKey(0)) continue;
            var rgba = new byte[checked(record.Width * record.Height * 4)];
            decodedBytes += rgba.Length;
            if (decodedBytes > 512 * 1024 * 1024) throw new InvalidDataException("PSD/PSB decoded layers exceed the 512 MiB import budget.");
            var first = decodedChannels[0];
            decodedChannels.TryGetValue(1, out var green);
            decodedChannels.TryGetValue(2, out var blue);
            decodedChannels.TryGetValue(-1, out var alpha);
            for (var pixel = 0; pixel < record.Width * record.Height; pixel++)
            {
                rgba[pixel * 4] = first[pixel];
                rgba[pixel * 4 + 1] = colorMode == 1 ? first[pixel] : green?[pixel] ?? first[pixel];
                rgba[pixel * 4 + 2] = colorMode == 1 ? first[pixel] : blue?[pixel] ?? first[pixel];
                rgba[pixel * 4 + 3] = alpha?[pixel] ?? (byte)255;
            }
            colors.Convert(rgba, decodedChannels, precision);
            if (record.RealMask is not null && decodedChannels.TryGetValue(-3, out var realMask))
                ApplyMask(rgba, record, record.RealMask, realMask);
            else if (record.Mask is not null && decodedChannels.TryGetValue(-2, out var maskPixels))
                ApplyMask(rgba, record, record.Mask, maskPixels);
            outputIndices.Add(recordIndex, result.Layers.Count);
            result.Layers.Add(new DecodedArtLayer
            {
                StableId = record.StableId, Name = record.Name, X = record.Left, Y = record.Top,
                Width = record.Width, Height = record.Height, Opacity = record.Opacity,
                Visible = record.Visible, Rgba = rgba, PrecisionChannels = precision, BlendMode = record.BlendMode
            });
        }
        foreach (var (source, output) in outputIndices)
            result.Layers[output].ParentIndex = parents[source] < 0 ? -1 : outputIndices[parents[source]];
    }

    private static LayerRecord ReadLayerRecord(ref BigEndianReader reader, bool psb, int index, DecodedArtDocument result)
    {
        var top = reader.I32(); var left = reader.I32(); var bottom = reader.I32(); var right = reader.I32();
        ValidateBounds(left, top, right, bottom);
        var channelCount = reader.U16();
        if (channelCount > 56) throw new InvalidDataException($"PSD/PSB layer {index} has too many channels.");
        var channelRecords = new List<ChannelRecord>(channelCount);
        for (var channel = 0; channel < channelCount; channel++)
            channelRecords.Add(new ChannelRecord(reader.I16(), psb ? reader.U64() : reader.U32()));
        if (reader.Ascii(4) is not ("8BIM" or "8B64")) throw new InvalidDataException("PSD/PSB layer blend signature is invalid.");
        var blendMode = reader.Ascii(4);
        var opacity = reader.Byte(); reader.Skip(1); var flags = reader.Byte(); reader.Skip(1);
        var extraLength = reader.Length32("layer extra data");
        var rawExtra = reader.Slice(reader.CheckedLength(extraLength, "layer extra data"));
        result.Metadata.Add(new() { Scope = "Photoshop.Layer:" + index, DataBase64 = Convert.ToBase64String(rawExtra) });
        var extra = new BigEndianReader(rawExtra);
        var maskLength = extra.Length32("layer mask");
        var maskReader = new BigEndianReader(extra.Slice(extra.CheckedLength(maskLength, "layer mask")));
        MaskRecord? mask = null; MaskRecord? realMask = null;
        if (maskLength > 0)
        {
            mask = ReadMask(ref maskReader);
            if ((mask.Flags & 16) != 0)
            {
                var parameters = maskReader.Byte();
                if ((parameters & 1) != 0) mask = mask with { Density = maskReader.Byte() };
                if ((parameters & 2) != 0) maskReader.Skip(8);
                if ((parameters & 4) != 0) maskReader.Skip(1);
                if ((parameters & 8) != 0) maskReader.Skip(8);
            }
            if (maskLength != 20 && maskReader.Remaining >= 18)
            {
                var realFlags = maskReader.Byte(); var background = maskReader.Byte();
                var realTop = maskReader.I32(); var realLeft = maskReader.I32();
                var realBottom = maskReader.I32(); var realRight = maskReader.I32();
                ValidateBounds(realLeft, realTop, realRight, realBottom);
                realMask = new MaskRecord(realLeft, realTop, realRight - realLeft, realBottom - realTop, background, realFlags, mask.Density);
            }
        }
        extra.SkipLength32("layer blending ranges");
        var pascalStart = extra.Position;
        var nameLength = extra.Byte();
        var name = Encoding.UTF8.GetString(extra.Slice(nameLength));
        extra.Skip((4 - ((extra.Position - pascalStart) & 3)) & 3);
        var stableId = index + 1;
        var sectionType = 0;
        while (extra.Remaining >= 12)
        {
            var signature = extra.Ascii(4);
            if (signature is not ("8BIM" or "8B64")) break;
            var key = extra.Ascii(4);
            var length = ReadTaggedLength(ref extra, signature, key, psb);
            var tagged = new BigEndianReader(extra.Slice(extra.CheckedLength(length, "layer tagged block")));
            if (key == "lyid" && length >= 4) stableId = tagged.I32();
            else if (key is "lsct" or "lsdk" && length >= 4)
            {
                sectionType = tagged.I32();
                if (sectionType is < 0 or > 3) throw new InvalidDataException("PSD section divider type is invalid.");
                if (length >= 12) { tagged.Skip(4); blendMode = tagged.Ascii(4); }
            }
            else if (key == "luni" && length >= 4)
            {
                var chars = checked((int)tagged.U32());
                if ((long)chars * 2 <= tagged.Remaining)
                {
                    var data = tagged.Slice(chars * 2);
                    name = Encoding.BigEndianUnicode.GetString(data);
                }
            }
            if ((length & 1) != 0 && extra.Remaining > 0) extra.Skip(1);
        }
        name = string.IsNullOrWhiteSpace(name) ? $"Layer {index + 1}" : name.Trim();
        return new LayerRecord(stableId, name, left, top, right - left, bottom - top,
            opacity, (flags & 2) == 0, channelRecords, mask, realMask) { BlendMode = blendMode, SectionType = sectionType };
    }

    private static ulong ReadTaggedLength(ref BigEndianReader reader, string signature, string key, bool psb) =>
        signature == "8B64" || (psb && key is "LMsk" or "Lr16" or "Lr32" or "Layr" or "Mt16" or "Mt32" or "Mtrn" or "Alph" or "FMsk" or "lnk2" or "FEid" or "FXid" or "PxSD")
            ? reader.U64() : reader.U32();

    private static void ValidateBounds(int left, int top, int right, int bottom)
    {
        var width = (long)right - left; var height = (long)bottom - top;
        if (width < 0 || height < 0 || width > 16384 || height > 16384 || width * height > 67_108_864)
            throw new InvalidDataException("PSD/PSB layer or mask has invalid or excessive bounds.");
    }

    private static MaskRecord ReadMask(ref BigEndianReader reader)
    {
        var top = reader.I32(); var left = reader.I32(); var bottom = reader.I32(); var right = reader.I32();
        ValidateBounds(left, top, right, bottom);
        return new MaskRecord(left, top, right - left, bottom - top, reader.Byte(), reader.Byte(), 255);
    }

    private static void ApplyMask(byte[] rgba, LayerRecord layer, MaskRecord mask, byte[] pixels)
    {
        if ((mask.Flags & 2) != 0) return;
        var left = (long)mask.Left + ((mask.Flags & 1) != 0 ? layer.Left : 0);
        var top = (long)mask.Top + ((mask.Flags & 1) != 0 ? layer.Top : 0);
        for (var y = 0; y < layer.Height; y++)
        for (var x = 0; x < layer.Width; x++)
        {
            var mx = layer.Left + (long)x - left; var my = layer.Top + (long)y - top;
            var value = mx >= 0 && mx < mask.Width && my >= 0 && my < mask.Height
                ? pixels[(int)my * mask.Width + (int)mx] : mask.DefaultColor;
            if ((mask.Flags & 4) != 0) value = (byte)(255 - value);
            var coverage = 255 - (255 - value) * mask.Density / 255;
            var offset = (y * layer.Width + x) * 4 + 3;
            rgba[offset] = (byte)((rgba[offset] * coverage + 127) / 255);
        }
    }

    private static byte[] DecodeChannel(ReadOnlySpan<byte> block, int width, int height, bool psb, int depth, out byte[] rawSamples)
    {
        var reader = new BigEndianReader(block);
        var compression = reader.U16();
        var rowBytes = checked(width * (depth / 8));
        var expected = checked(rowBytes * height);
        rawSamples = [];
        if (expected == 0) return [];
        byte[] output;
        if (compression == 0)
        {
            if (reader.Remaining < expected) throw new InvalidDataException("Raw PSD channel is truncated.");
            output = reader.Slice(expected).ToArray();
        }
        else if (compression == 1)
        {
            var lengths = new int[height];
            for (var row = 0; row < height; row++) lengths[row] = psb ? checked((int)reader.U32()) : reader.U16();
            output = new byte[expected];
            for (var row = 0; row < height; row++) DecodePackBits(reader.Slice(lengths[row]), output.AsSpan(row * rowBytes, rowBytes));
        }
        else if (compression is 2 or 3)
        {
            using var input = new MemoryStream(reader.Slice(reader.Remaining).ToArray(), writable: false);
            using var zip = new ZLibStream(input, CompressionMode.Decompress);
            output = new byte[expected];
            zip.ReadExactly(output);
            if (zip.ReadByte() >= 0) throw new InvalidDataException("ZIP PSD channel contains excess decoded data.");
            if (compression == 3) UndoPrediction(output, width, height, depth);
        }
        else throw new NotSupportedException($"PSD channel compression {compression} is not supported.");
        rawSamples = output;
        if (depth == 8) return output;
        var samples = new byte[checked(width * height)];
        for (var pixel = 0; pixel < samples.Length; pixel++)
        {
            if (depth == 16) samples[pixel] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(pixel * 2)) + 128) / 257);
            else
            {
                var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(output.AsSpan(pixel * 4)));
                if (!float.IsFinite(value)) throw new InvalidDataException("PSD floating-point channel contains non-finite pixels.");
                samples[pixel] = (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);
            }
        }
        return samples;
    }

    private static void UndoPrediction(byte[] bytes, int width, int height, int depth)
    {
        var rowBytes = width * (depth / 8);
        var unshuffled = depth == 32 ? new byte[rowBytes] : [];
        for (var y = 0; y < height; y++)
        {
            var row = bytes.AsSpan(y * rowBytes, rowBytes);
            if (depth == 16)
            {
                for (var x = 1; x < width; x++)
                {
                    var sum = unchecked((ushort)(BinaryPrimitives.ReadUInt16BigEndian(row[(x * 2)..]) + BinaryPrimitives.ReadUInt16BigEndian(row[((x - 1) * 2)..])));
                    BinaryPrimitives.WriteUInt16BigEndian(row[(x * 2)..], sum);
                }
            }
            else
            {
                for (var x = 1; x < rowBytes; x++) row[x] = unchecked((byte)(row[x] + row[x - 1]));
                if (depth == 32)
                {
                    for (var x = 0; x < width; x++)
                    for (var plane = 0; plane < 4; plane++) unshuffled[x * 4 + plane] = row[plane * width + x];
                    unshuffled.CopyTo(row);
                }
            }
        }
    }

    private static void DecodePackBits(ReadOnlySpan<byte> encoded, Span<byte> output)
    {
        var input = 0; var target = 0;
        while (target < output.Length && input < encoded.Length)
        {
            var control = unchecked((sbyte)encoded[input++]);
            if (control >= 0)
            {
                var count = control + 1;
                if (input + count > encoded.Length || target + count > output.Length) throw new InvalidDataException("PSD PackBits literal exceeds its row.");
                encoded.Slice(input, count).CopyTo(output[target..]); input += count; target += count;
            }
            else if (control != -128)
            {
                var count = 1 - control;
                if (input >= encoded.Length || target + count > output.Length) throw new InvalidDataException("PSD PackBits run exceeds its row.");
                output.Slice(target, count).Fill(encoded[input++]); target += count;
            }
        }
        if (target != output.Length) throw new InvalidDataException("PSD PackBits row decoded to the wrong width.");
    }

    private readonly record struct ChannelRecord(short Id, ulong Length);
    private sealed record LayerRecord(int StableId, string Name, int Left, int Top, int Width, int Height,
        byte Opacity, bool Visible, List<ChannelRecord> Channels, MaskRecord? Mask, MaskRecord? RealMask)
    { internal string BlendMode { get; init; } = "norm"; internal int SectionType { get; init; } }
    private sealed record MaskRecord(int Left, int Top, int Width, int Height, byte DefaultColor, byte Flags, byte Density);

    private ref struct BigEndianReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        internal int Position { get; set; }
        internal int Remaining => _bytes.Length - Position;
        internal byte Byte() { Require(1); return _bytes[Position++]; }
        internal ushort U16() { Require(2); var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(_bytes[Position..]); Position += 2; return value; }
        internal short I16() => unchecked((short)U16());
        internal uint U32() { Require(4); var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(_bytes[Position..]); Position += 4; return value; }
        internal int I32() => unchecked((int)U32());
        internal ulong U64() { Require(8); var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(_bytes[Position..]); Position += 8; return value; }
        internal string Ascii(int count) => Encoding.ASCII.GetString(Slice(count));
        internal ReadOnlySpan<byte> Slice(int count) { Require(count); var value = _bytes.Slice(Position, count); Position += count; return value; }
        internal void Skip(int count) { Require(count); Position += count; }
        internal void SkipLength32(string name) { var length = Length32(name); Skip(CheckedLength(length, name)); }
        internal ulong Length32(string _) => U32();
        internal ulong Length64(string _) => U64();
        internal int CheckedLength(ulong length, string name) => length <= int.MaxValue && length <= (ulong)Remaining ? (int)length : throw new InvalidDataException($"PSD/PSB {name} length is invalid.");
        internal int CheckedEnd(ulong length, string name) => checked(Position + CheckedLength(length, name));
        private void Require(int count) { if (count < 0 || count > Remaining) throw new InvalidDataException("PSD/PSB data is truncated."); }
    }
}
