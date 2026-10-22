namespace BEngine.ProjectSystem;

internal sealed class PackageCompilationResult
{
    public IReadOnlyDictionary<string, string> AssemblyPaths { get; }

    internal PackageCompilationResult(IReadOnlyDictionary<string, string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        AssemblyPaths = new Dictionary<string, string>(assemblyPaths, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetAssemblyPath(string assemblyName, out string path) =>
        AssemblyPaths.TryGetValue(assemblyName, out path!);
}
