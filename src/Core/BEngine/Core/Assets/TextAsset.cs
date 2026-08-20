namespace BEngine;

public sealed class TextAsset : BAsset
{
    private readonly string _text;
    private readonly byte[] _bytes;
    private readonly string _path;

    public TextAsset(string text, string sourcePath)
    {
        _text = text;
        _bytes = System.Text.Encoding.UTF8.GetBytes(text);
        name = Path.GetFileNameWithoutExtension(sourcePath);
        _path = sourcePath;
    }

    public string text
    {
        get { MainThreadGuard.Ensure(); return _text; }
    }
    public byte[] bytes
    {
        get { MainThreadGuard.Ensure(); return (byte[])_bytes.Clone(); }
    }
    public string path
    {
        get { MainThreadGuard.Ensure(); return _path; }
    }
}
