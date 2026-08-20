using System.Security.Cryptography;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public enum AssetChangeKind
{
    Imported,
    Updated,
    Deleted,
    Moved
}
