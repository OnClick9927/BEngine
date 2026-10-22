using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;

namespace BEngine.Player;

internal sealed class PlayerPackageDependency
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = "runtime";
}
