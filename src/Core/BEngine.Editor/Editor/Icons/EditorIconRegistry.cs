using System.Reflection;

namespace BEngine.Editor;

public static class EditorIconRegistry
{
    private static readonly Dictionary<string, (string Path, Assembly Owner)> Names = new(StringComparer.OrdinalIgnoreCase);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void RegisterName(string name, string path) { lock (Gate) Names[name] = (path, Assembly.GetCallingAssembly()); }
    internal static string? ResolveName(string name) { lock (Gate) return Names.TryGetValue(name, out var entry) ? entry.Path : null; }
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
        if (typeof(SpriteMask).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.SpriteMask;
        if (typeof(Transform).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Transform;
        if (typeof(MonoBehaviour).IsAssignableFrom(type)) return EditorBuiltinIcons.Components.Script;
        return EditorBuiltinIcons.Components.Default;
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Gate)
        {
            Registrations.RemoveAll(item => item.Type.Assembly == assembly);
            foreach (var entry in Names.Where(p => p.Value.Owner == assembly).ToArray()) Names.Remove(entry.Key);
            Resolved.Clear();
        }
    }

    private readonly record struct Registration(Type Type, string ResourcePath, bool UseForChildren);
}
