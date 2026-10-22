using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;

namespace BEngine.Editor;

internal static class PlayerBuildServices
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register package build services.")]
    internal static void Initialize() => RuntimeServiceContributions.Register(services => services.AddBEngineBuildPipelines());
}
