using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

public class DefaultAsset : BObject
{
    public string assetPath { get; internal set; } = string.Empty;
    public string sourcePath { get; internal set; } = string.Empty;
    public string guid { get; internal set; } = string.Empty;
    public string assetType { get; internal set; } = string.Empty;
    public string packageId { get; internal set; } = string.Empty;
    public string packageVersion { get; internal set; } = string.Empty;
}
