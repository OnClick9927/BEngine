using System.Reflection;

namespace BEngine.Editor;

public static class EditorIconRegistry
{
    private static readonly Lock Gate = new();
    private static readonly List<Registration> Registrations = [];
    private static readonly Dictionary<Type, string?> Resolved = [];

    public static void Register(Type type, string resourcePath, bool useForChildren = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        lock (Gate)
        {
            Registrations.RemoveAll(item => item.Type == type);
            Registrations.Add(new Registration(type, resourcePath, useForChildren));
            Resolved.Clear();
        }
    }

    public static string? GetIconPath(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (Gate)
        {
            if (Resolved.TryGetValue(type, out var cached)) return cached;
            Registration? registration = Registrations
                .Where(item => item.Type == type || item.UseForChildren && item.Type.IsAssignableFrom(type))
                .OrderByDescending(item => item.Type == type)
                .Cast<Registration?>().FirstOrDefault();
            var resourcePath = registration?.ResourcePath ??
                               type.GetCustomAttribute<EditorIconAttribute>(inherit: true)?.resourcePath;
            return Resolved[type] = string.IsNullOrWhiteSpace(resourcePath)
                ? null
                : EditorResource.FindPath(resourcePath) ?? resourcePath;
        }
    }

    public static string GetComponentIconPath(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var registered = GetIconPath(type);
        if (!string.IsNullOrWhiteSpace(registered)) return registered;
        if (typeof(Camera2D).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Camera2D;
        if (typeof(SpriteRenderer).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.SpriteRenderer;
        if (typeof(Transform).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Transform;
        var builtIn = type.Name switch
        {
            nameof(ParticleSystem2D) => EditorBuiltinIcons.Components.ParticleSystem2D,
            nameof(EditorBuiltinIcons.Components.Rigidbody2D) => EditorBuiltinIcons.Components.Rigidbody2D,
            nameof(EditorBuiltinIcons.Components.Collider2D) or nameof(EditorBuiltinIcons.Components.BoxCollider2D)
                or nameof(EditorBuiltinIcons.Components.CircleCollider2D)
                or nameof(EditorBuiltinIcons.Components.CapsuleCollider2D)
                or nameof(EditorBuiltinIcons.Components.PolygonCollider2D) =>
                EditorBuiltinIcons.Components.Collider2D,
            nameof(EditorBuiltinIcons.Components.Animator) or nameof(EditorBuiltinIcons.Components.Animation) =>
                EditorBuiltinIcons.Components.Animator,
            nameof(EditorBuiltinIcons.Components.Tilemap) or nameof(EditorBuiltinIcons.Components.TilemapRenderer) =>
                EditorBuiltinIcons.Components.Tilemap,
            nameof(EditorBuiltinIcons.Components.UIDocument) => EditorBuiltinIcons.Components.UIDocument,
            var name when name.StartsWith("Navigation", StringComparison.Ordinal) =>
                EditorBuiltinIcons.Components.Navigation,
            _ => null
        };
        if (builtIn is not null) return builtIn;
        if (typeof(MonoBehaviour).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Script;
        return EditorBuiltinIcons.Components.Default;
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Gate)
        {
            Registrations.RemoveAll(item => item.Type.Assembly == assembly);
            Resolved.Clear();
        }
    }

    private readonly record struct Registration(Type Type, string ResourcePath, bool UseForChildren);
}
