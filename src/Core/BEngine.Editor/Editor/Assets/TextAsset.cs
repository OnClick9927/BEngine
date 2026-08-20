using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

public class TextAsset : DefaultAsset
{
    public string text { get; internal set; } = string.Empty;
    public byte[] bytes => System.Text.Encoding.UTF8.GetBytes(text);
}
