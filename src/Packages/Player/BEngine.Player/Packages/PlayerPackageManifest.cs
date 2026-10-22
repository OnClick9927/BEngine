using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;

namespace BEngine.Player;

internal sealed class PlayerPackageManifest
{
    public List<PlayerPackageReference> Packages { get; set; } = [];
}
