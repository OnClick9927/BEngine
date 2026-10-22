namespace BEngine;

[EditorIcon("Icons/Assets/AssetText.png")]
public sealed class TextAsset : BAsset
{
    internal static string Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private string _text;
    private byte[] _bytes;
    private readonly string _path;

    public TextAsset(string text, string sourcePath)
    {
        _text = text;
        _bytes = System.Text.Encoding.UTF8.GetBytes(text);
        name = RuntimeServiceAccess.PathService.GetFileNameWithoutExtension(sourcePath);
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
