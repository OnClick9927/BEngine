using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.Navigation2D;

internal static class Navigation2DPackageRegistration
{
    [ModuleInitializer]
    internal static void RegisterLegacyTypes() =>
        ComponentTypeMigrationRegistry.RegisterPrefix("BEngine.AI2D.", "BEngine.Navigation2D.");
}
