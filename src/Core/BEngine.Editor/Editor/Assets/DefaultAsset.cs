using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

[EditorIcon("Icons/Assets/AssetDefault.png")]
public class DefaultAsset : BAsset
{
    public string packageId { get; internal set; } = string.Empty;
    public string packageVersion { get; internal set; } = string.Empty;
}
