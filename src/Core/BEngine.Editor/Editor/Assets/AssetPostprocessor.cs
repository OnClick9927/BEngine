using System.Reflection;

namespace BEngine.Editor;

public abstract class AssetPostprocessor
{
    public string assetPath { get; internal set; } = string.Empty;
    public AssetImporter? assetImporter { get; internal set; }
    public virtual uint GetVersion() => 0;
    public virtual int GetPostprocessOrder() => 0;
}
