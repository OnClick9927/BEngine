using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

[Flags]
public enum ImportAssetOptions
{
    Default = 0,
    ForceUpdate = 1,
    ForceSynchronousImport = 8,
    ImportRecursive = 256,
    DontDownloadFromCacheServer = 8192,
    ForceUncompressedImport = 16384
}
