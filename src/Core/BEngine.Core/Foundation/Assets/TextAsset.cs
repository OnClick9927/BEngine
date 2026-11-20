namespace BEngine;

public sealed class TextAsset : BAsset
{
    internal static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes, "buffer");
        return Decode(bytes.AsSpan());
    }

    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        // Match StreamReader BOM detection, including UTF-32 before UTF-16 LE.
        // Decoding the final span also preserves replacement of incomplete sequences.
        if (bytes.StartsWith<byte>([0xEF, 0xBB, 0xBF])) return System.Text.Encoding.UTF8.GetString(bytes[3..]);
        if (bytes.StartsWith<byte>([0xFF, 0xFE, 0x00, 0x00])) return System.Text.Encoding.UTF32.GetString(bytes[4..]);
        if (bytes.StartsWith<byte>([0x00, 0x00, 0xFE, 0xFF])) return System.Text.Encoding.GetEncoding(12001).GetString(bytes[4..]);
        if (bytes.StartsWith<byte>([0xFF, 0xFE])) return System.Text.Encoding.Unicode.GetString(bytes[2..]);
        if (bytes.StartsWith<byte>([0xFE, 0xFF])) return System.Text.Encoding.BigEndianUnicode.GetString(bytes[2..]);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
    private string _text;
    private byte[] _bytes;
    private readonly string _path;

    public TextAsset(string text, string sourcePath)
    {
        _text = text;
        _bytes = System.Text.Encoding.UTF8.GetBytes(text);
        name = RuntimeServices.PathService.GetFileNameWithoutExtension(sourcePath);
        _path = sourcePath;
    }

    public string text
    {
        get { ThrowIfDestroyed(); return _text; }
    }
    public byte[] bytes
    {
        get { ThrowIfDestroyed(); return (byte[])_bytes.Clone(); }
    }
    public string path
    {
        get { return _path; }
    }

    protected override void ReleaseAssetResources() { _text = string.Empty; _bytes = []; }
}
