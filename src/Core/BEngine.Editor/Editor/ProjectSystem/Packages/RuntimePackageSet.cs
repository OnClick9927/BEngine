using System.Reflection;
using System.Runtime.Loader;
using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.ProjectSystem;

public sealed class RuntimePackageSet
{
    private readonly HashSet<string> _enabledPackageIds;

    internal RuntimePackageSet(
        IEnumerable<string> enabledPackageIds,
        IReadOnlyDictionary<string, string> assemblyReferences)
    {
        _enabledPackageIds = new HashSet<string>(enabledPackageIds, StringComparer.OrdinalIgnoreCase);
        AssemblyReferences = assemblyReferences;
    }

    public IReadOnlyDictionary<string, string> AssemblyReferences { get; }

    public bool IsEnabled(string packageId) => _enabledPackageIds.Contains(packageId);
}
