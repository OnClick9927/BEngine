using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;

namespace BEngine.Player;

internal sealed class PlayerPackageDefinition
{
    public string Id { get; set; } = string.Empty;
    public PlayerPackageAssembly? Runtime { get; set; }
}
