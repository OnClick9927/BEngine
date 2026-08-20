using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Player;

internal sealed class PlayerPackageDependency : Document
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = "runtime";
}
