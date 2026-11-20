using BEngine.Platform;

namespace BEngine.ProjectSystem;

/// <summary>Source compilation for authoring tools. The caller supplies the exact dependency
/// closure and owns the working directory and its generated outputs; no project is opened,
/// no assemblies are loaded and no editor-instance cache is reused.</summary>
public static class PackageSourceValidation
{
    /// <summary>Compiles Runtime before Editor, in dependency order, into the working
    /// directory's Library. Source packages remain untouched. Missing/invalid dependencies,
    /// compiler failures and cancellation are reported to the caller before it publishes.
    /// Existing project scripts are never included as assembly references.</summary>
    public static void Compile(string workingDirectory, IEnumerable<BPackageDefinition> definitions,
        Action<int, int, string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(definitions);
        cancellationToken.ThrowIfCancellationRequested();
        var files = RuntimeServices.FileSystem;
        var paths = RuntimeServices.PathService;
        var workspace = new ProjectWorkspace(paths.GetFullPath(workingDirectory),
            new ProjectData { Name = "Package validation" }, files, paths);
        PackageSourceCompiler.CompileEnabled(workspace, definitions, ProcessUtility.Current,
            progress, isolated: true, cancellationToken: cancellationToken);
    }
}
