using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;

namespace BEngine.Player;

internal sealed class PlayerPackageAssembly
{
    public string Assembly { get; set; } = string.Empty;
    public List<PlayerPackageDependency> Dependencies { get; set; } = [];
}
