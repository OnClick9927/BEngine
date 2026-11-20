using BEngine.Platform;

namespace BEngine.ProjectSystem;

internal sealed class PackageSourceCompilerService : IPackageSourceCompiler
{
    private readonly ProcessUtility _processRunner;

    public PackageSourceCompilerService(ProcessUtility processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public PackageCompilationResult CompileEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions,
        Action<int, int, string>? progress = null) =>
        PackageSourceCompiler.CompileEnabled(workspace, definitions, _processRunner, progress);
}
