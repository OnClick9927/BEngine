using System.Runtime.CompilerServices;
namespace BEngine.Editor;
internal static class PackageIconAliases
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register package icons.")]
    internal static void Register()
    {
        EditorIconRegistry.RegisterName("Rigidbody2D Icon", "Icons/Components/Rigidbody2D.png");
        EditorIconRegistry.RegisterName("Collider2D Icon", "Icons/Components/Collider2D.png");
        EditorIconRegistry.RegisterName("BoxCollider2D Icon", "Icons/Components/Collider2D.png");
        EditorIconRegistry.RegisterName("CircleCollider2D Icon", "Icons/Components/Collider2D.png");
        EditorIconRegistry.RegisterName("CapsuleCollider2D Icon", "Icons/Components/Collider2D.png");
        EditorIconRegistry.RegisterName("PolygonCollider2D Icon", "Icons/Components/Collider2D.png");
    }
}
