using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

public class DefaultAsset : FileAsset
{
    public string packageId { get; internal set; } = string.Empty;
    public string packageVersion { get; internal set; } = string.Empty;
}
