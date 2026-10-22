using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;

namespace BEngine.Player;

internal sealed class PlayerPackageReference
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
