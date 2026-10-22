using System.Runtime.CompilerServices;
using BEngine.ProjectSystem.Editor;
namespace BEngine.Editor;
internal static class PlayerProjectRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register Player project constraints.")]
    internal static void Register()
    {
        AssetProtection.ProtectedPath += AotProjectLayout.IsProtectedAssetPath;
        AssetProtection.AssemblyIdentity += path => path.Equals(AotProjectLayout.AssemblyDefinitionAssetPath, StringComparison.OrdinalIgnoreCase) ? "AOT" : null;
    }
}
