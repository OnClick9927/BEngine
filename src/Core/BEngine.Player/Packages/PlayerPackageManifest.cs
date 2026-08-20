using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Player;

internal sealed class PlayerPackageManifest : Document
{
    public List<PlayerPackageReference> Packages { get; set; } = [];
}
