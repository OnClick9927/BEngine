using System.Runtime.CompilerServices;
namespace BEngine.Editor;
internal static class PackageIconAliases
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register package icons.")]
    internal static void Register()
    {
        EditorIconRegistry.RegisterName("NavMeshAgent Icon", "Icons/Components/Navigation.png");
    }
}
