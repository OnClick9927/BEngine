namespace BEngine.Editor.Build.ProjectSystem;
public sealed record RuntimeManagedCodeReleaseInput(
    string Name,
    string BuildId,
    string AssemblyPath,
    string? SymbolsPath,
    string AssemblySha256,
    long AssemblySize,
    IReadOnlyList<string> Dependencies,
    string Origin);

public sealed record RuntimePackageResourceInput(
    string PackageId,
    string Address,
    string FilePath,
    string Sha256,
    long Size);

public sealed class RuntimeManagedCodeReleaseInputSet
{
    public RuntimeManagedCodeReleaseInputSet(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> assemblies,
        IReadOnlyList<RuntimePackageResourceInput> packageResources,
        IReadOnlyList<string> packageResourceRoots)
    {
        Assemblies = assemblies;
        PackageResources = packageResources;
        PackageResourceRoots = packageResourceRoots;
    }

    public IReadOnlyList<RuntimeManagedCodeReleaseInput> Assemblies { get; }
    public IReadOnlyList<RuntimePackageResourceInput> PackageResources { get; }
    public IReadOnlyList<string> PackageResourceRoots { get; }
}

