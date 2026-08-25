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
            return Resolved[type] = registration is null ? null : EditorResources.FindPath(registration.Value.ResourcePath);
        }
    }

    public static string GetComponentIconPath(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var registered = GetIconPath(type);
        if (!string.IsNullOrWhiteSpace(registered)) return registered;
        if (typeof(Camera2D).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Camera2D;
        if (typeof(SpriteRenderer).IsAssignableFrom(type)) return EditorBuiltinIcons.Assets.Image;
        if (typeof(MonoBehaviour).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Script;
        if (typeof(Transform).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Transform;
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
