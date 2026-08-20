using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.Animation;

internal static class AnimationPackageRegistration
{
    [ModuleInitializer]
    internal static void RegisterLegacyTypes()
    {
        ComponentTypeMigrationRegistry.Register("BEngine.Animator", typeof(Animator).FullName!);
        ComponentTypeMigrationRegistry.Register("BEngine.Animation", typeof(Animation).FullName!);
    }
}
