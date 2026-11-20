using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using BEngine;
using BEngine.Editor;

namespace ArtOwnershipTests;

public static class ArtFixtures
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Assembly Art = typeof(ArtDocumentImporter).Assembly;
    private static readonly Type Document = Art.GetType("BEngine.Editor.DecodedArtDocument", true)!;
    public static readonly Type Transform = Art.GetType("BEngine.Editor.PhotoshopColorTransform", true)!;
    private static readonly MethodInfo OpenMethod = Transform.GetMethod("Open", Flags)!;
    private static readonly MethodInfo ConvertMethod = Transform.GetMethod("Convert", Flags)!;
    public static byte[] Srgb => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool/drivers/color/sRGB Color Space Profile.icm"));
    public static byte[] Cmyk => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool/drivers/color/RSWOP.icm"));
    public static byte[] GrayProfile()
    {
        var bytes = new byte[364];
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), value);
        void Tag(int at, string value) => Encoding.ASCII.GetBytes(value).CopyTo(bytes, at);
        U32(0, (uint)bytes.Length); U32(8, 0x02100000); Tag(12, "mntr"); Tag(16, "GRAY"); Tag(20, "XYZ ");
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(24), 2026); BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(26), 1); BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(28), 1);
        Tag(36, "acsp"); Tag(40, "MSFT"); U32(68, 63190); U32(72, 65536); U32(76, 54061);
        U32(128, 4); Tag(132, "wtpt"); U32(136, 180); U32(140, 20); Tag(144, "kTRC"); U32(148, 200); U32(152, 14);
        Tag(156, "desc"); U32(160, 216); U32(164, 112); Tag(168, "cprt"); U32(172, 328); U32(176, 36);
        Tag(180, "XYZ "); U32(188, 63190); U32(192, 65536); U32(196, 54061);
        Tag(200, "curv"); U32(208, 1); BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(212), 563);
        Tag(216, "desc"); U32(224, 15); Tag(228, "Gray gamma 2.2\0");
        Tag(328, "text"); Tag(336, "Public domain test fixture\0");
        return bytes;
    }
    private delegate object DecodeDelegate(ReadOnlySpan<byte> bytes);
    public static byte[] DecodePixels(byte[] source)
    {
        var decoder = Art.GetType("BEngine.Editor.PhotoshopDecoder", true)!.GetMethod("Decode", Flags)!.CreateDelegate<DecodeDelegate>();
        var document = decoder(source);
        var layers = (IList)Document.GetProperty("Layers", Flags)!.GetValue(document)!;
        return (byte[])layers[0]!.GetType().GetProperty("Rgba", Flags)!.GetValue(layers[0])!;
    }
    public static object MakeDocument(params ArtSourceMetadata[] metadata)
    {
        var doc = Activator.CreateInstance(Document, true)!;
        Document.GetProperty("Kind", Flags)!.SetValue(doc, ArtDocumentSourceKind.Photoshop);
        Document.GetProperty("Width", Flags)!.SetValue(doc, 1);
        Document.GetProperty("Height", Flags)!.SetValue(doc, 1);
        var entries = (IList)Document.GetProperty("Metadata", Flags)!.GetValue(doc)!;
        foreach (var entry in metadata) entries.Add(entry);
        return doc;
    }
    public static ArtSourceMetadata Profile(byte[] bytes) => new() { Scope = "Photoshop.ImageResource", Type = 1039, DataBase64 = System.Convert.ToBase64String(bytes) };
    public static IDisposable Open(object document, int mode) => (IDisposable)Invoke(OpenMethod, null, [document, mode])!;
    public static void Convert(IDisposable transform, byte[] rgba, Dictionary<short, byte[]> channels, List<ArtPrecisionChannel> precision) => Invoke(ConvertMethod, transform, [rgba, channels, precision]);
    private static object? Invoke(MethodInfo method, object? owner, object?[] args)
    {
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
    public static (byte[] Rgba, Dictionary<short, byte[]> Channels, List<ArtPrecisionChannel> Precision) Pixels(int mode, int depth, int count)
    {
        var rgba = new byte[count * 4]; var channels = new Dictionary<short, byte[]>(); var precision = new List<ArtPrecisionChannel>();
        for (short channel = 0; channel < (mode == 4 ? 4 : mode == 1 ? 1 : 3); channel++)
        {
            var values = new byte[count]; var raw = depth == 8 ? [] : new byte[count * (depth / 8)];
            for (var i = 0; i < count; i++)
            {
                values[i] = (byte)((i * 37 + channel * 61 + 13) % 256);
                if (depth == 16) BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(i * 2), (ushort)(values[i] * 257));
                if (depth == 32) BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(i * 4), BitConverter.SingleToInt32Bits(values[i] / 255f));
            }
            channels.Add(channel, values);
            if (depth > 8) precision.Add(new() { Id = channel, Width = count, Height = 1, BitDepth = depth, SamplesBase64 = System.Convert.ToBase64String(raw) });
        }
        for (var i = 0; i < count; i++)
        {
            rgba[i * 4] = channels[0][i]; rgba[i * 4 + 1] = channels[mode == 1 ? (short)0 : (short)1][i];
            rgba[i * 4 + 2] = channels[mode == 1 ? (short)0 : (short)2][i]; rgba[i * 4 + 3] = (byte)(i * 17 + 9);
        }
        return (rgba, channels, precision);
    }
    public static byte[] Psd(byte[] profile, int width = 3, int depth = 8, bool large = false)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        void U16(int value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value); writer.Write(bytes); }
        void U32(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value); writer.Write(bytes); }
        void Size(int value) { if (large) U32(0); U32(value); }
        void Text(string text) => writer.Write(Encoding.ASCII.GetBytes(text));
        Text("8BPS"); U16(large ? 2 : 1); writer.Write(new byte[6]); U16(4); U32(1); U32(width); U16(depth); U16(3); U32(0);
        U32(12 + profile.Length + profile.Length % 2); Text("8BIM"); U16(1039); U16(0); U32(profile.Length); writer.Write(profile); if ((profile.Length & 1) != 0) writer.Write((byte)0);
        var layerSectionSize = output.Position; Size(0); var layerSectionStart = output.Position;
        var layerInfoSize = output.Position; Size(0); var layerInfoStart = output.Position;
        U16(1); U32(0); U32(0); U32(1); U32(width); U16(4);
        for (var channel = 0; channel < 4; channel++) { U16(channel == 3 ? 65535 : channel); Size(2 + width * depth / 8); }
        Text("8BIM"); Text("norm"); writer.Write(new byte[] { 255, 0, 0, 0 }); U32(12); U32(0); U32(0); writer.Write(new byte[] { 1, (byte)'B', 0, 0 });
        var pixels = Pixels(3, depth, width);
        for (var channel = 0; channel < 4; channel++)
        {
            U16(0);
            for (var pixel = 0; pixel < width; pixel++)
            {
                var value = channel == 3 ? pixels.Rgba[pixel * 4 + 3] : pixels.Channels[(short)channel][pixel];
                if (depth == 8) writer.Write(value);
                else if (depth == 16) U16(value * 257);
                else U32(BitConverter.SingleToInt32Bits(value / 255f));
            }
        }
        if (((output.Position - layerInfoStart) & 1) != 0) writer.Write((byte)0);
        var layerInfoEnd = output.Position; U32(0); var end = output.Position;
        output.Position = layerSectionSize; Size((int)(end - layerSectionStart));
        output.Position = layerInfoSize; Size((int)(layerInfoEnd - layerInfoStart)); output.Position = end;
        return output.ToArray();
    }
}
