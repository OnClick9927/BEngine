using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

/// <summary>The inclusive minimum engine version declared by a source package release.</summary>
public static class BPackageCompatibility
{
    public static bool SupportsEngine(PackageDefinitionDocument package, string engineVersion)
    {
        ArgumentNullException.ThrowIfNull(package);
        BPackageVersion.Validate(engineVersion);
        return BPackageVersion.Compare(engineVersion, package.MinimumEngineVersion) >= 0;
    }
}
