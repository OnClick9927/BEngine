using System.Runtime.CompilerServices;
using BEngine.ProjectSystem;
using BEngine.Editor.Build;
using BEngine.App.Editor.ProjectSystem;
namespace BEngine.App.Editor;
internal static class ApplicationEditorWorkflow
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register the game framework template policy.")]
    internal static void Register()
    {
        PlayerProjectPolicies.EnsureProject += PlayerProjectTemplate.EnsureRequiredAotInvariants;
        PlayerProjectPolicies.NormalizeBuildSettings += PlayerProjectTemplate.NormalizeBuildSettings;
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            if (assembly == typeof(ApplicationEditorWorkflow).Assembly)
            {
                PlayerProjectPolicies.EnsureProject -= PlayerProjectTemplate.EnsureRequiredAotInvariants;
                PlayerProjectPolicies.NormalizeBuildSettings -= PlayerProjectTemplate.NormalizeBuildSettings;
            }
        });
    }
}
