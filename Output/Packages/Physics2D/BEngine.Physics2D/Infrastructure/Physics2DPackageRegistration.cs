using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.Physics2D;

internal static class Physics2DPackageRegistration
{
    [ModuleInitializer]
    internal static void RegisterLegacyTypes()
    {
        foreach (var type in new[]
                 {
                     typeof(Rigidbody2D), typeof(Collider2D), typeof(BoxCollider2D),
                     typeof(CircleCollider2D), typeof(CapsuleCollider2D), typeof(PolygonCollider2D)
                 })
            ComponentTypeMigrationRegistry.Register($"BEngine.{type.Name}", type.FullName!);
    }
}
