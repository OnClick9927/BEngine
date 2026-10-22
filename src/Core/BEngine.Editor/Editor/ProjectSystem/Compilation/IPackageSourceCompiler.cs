namespace BEngine.ProjectSystem;

internal interface IPackageSourceCompiler
{
    PackageCompilationResult CompileEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions,
        Action<int, int, string>? progress = null);
}
