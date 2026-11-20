using System.Reflection;

namespace BEngine.Editor;

[Flags]
public enum RemoveAssetOptions
{
    None = 0,
    MoveAssetToTrash = 1
}
