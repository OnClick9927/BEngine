using BEngine.ProjectSystem.Editor;

namespace BEngine.ProjectSystem;

/// <summary>Creates an activated package manager using the host's compilation services.
/// The caller owns and must dispose the returned manager; the factory owns no project.</summary>
public interface IProjectPackageManagerFactory
{
    BPackageManager Create(ProjectWorkspace workspace, BPackageCatalog? catalog = null);
}

internal sealed class ProjectPackageManagerFactory(IPackageSourceCompiler compiler) : IProjectPackageManagerFactory
{
    public BPackageManager Create(ProjectWorkspace workspace, BPackageCatalog? catalog = null) =>
        new(workspace, catalog, loadAssemblies: true, compiler);
}
