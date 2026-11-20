using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

public class TextAsset : DefaultAsset
{
    public TextAsset() { }

    /// <summary>Creates a text representation for a source-format provider.</summary>
    public TextAsset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        this.text = text;
    }

    public string text { get; internal set; } = string.Empty;
    public byte[] bytes => System.Text.Encoding.UTF8.GetBytes(text);
}
