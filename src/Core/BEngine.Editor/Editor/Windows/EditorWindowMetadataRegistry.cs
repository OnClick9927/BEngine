using System.Reflection;
using System.Linq.Expressions;

namespace BEngine.Editor;

internal static class EditorWindowMetadataRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, WindowMetadata> Metadata = [];
    private static int _generation = -1;

    internal static void Warmup()
    {
        lock (Gate) EnsureFresh();
    }

    internal static string? GetIcon(Type windowType)
    {
        lock (Gate) { EnsureFresh(); return Metadata.GetValueOrDefault(windowType).Icon; }
    }

    internal static EditorWindowContextCommand[] GetContextCommands(Type windowType)
    {
        lock (Gate) { EnsureFresh(); return Metadata.GetValueOrDefault(windowType).Commands ?? []; }
    }

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        if (_generation == generation) return;
        Metadata.Clear();
        var contextMethods = TypeCache.GetMethodsWithAttribute<ContextMenuAttribute>();
        foreach (var type in TypeCache.GetTypesDerivedFrom<EditorWindow>().Where(type => !type.IsAbstract))
        {
            if (EditorFeatureGuard.TryInvoke($"EditorWindow metadata {type.FullName}",
                    () => CreateMetadata(type, contextMethods), default, out var metadata))
                Metadata[type] = metadata;
        }
        _generation = generation;
    }

    private static WindowMetadata CreateMetadata(Type type, IEnumerable<MethodInfo> contextMethods)
    {
        var icon = type.GetCustomAttribute<EditorWindowIconAttribute>()?.resourcePath;
        var commands = contextMethods.Where(method => method.DeclaringType?.IsAssignableFrom(type) == true)
            .SelectMany(method => method.GetCustomAttributes<ContextMenuAttribute>(inherit: false)
                .Select(attribute => (Method: method, Attribute: attribute)))
            .Where(item => IsValid(item.Method))
            .OrderBy(item => item.Attribute.itemName, StringComparer.Ordinal)
            .Select(item => new EditorWindowContextCommand(item.Attribute.itemName, item.Method.Name,
                Compile(item.Method)))
            .ToArray();
        return new WindowMetadata(icon, commands);
    }

    private static bool IsValid(MethodInfo method)
    {
        if (!method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0) return true;
        Debug.LogWarning($"ContextMenu method must be instance void with no parameters: " +
                         $"{method.DeclaringType?.FullName}.{method.Name}");
        return false;
    }

    private static Action<EditorWindow> Compile(MethodInfo method)
    {
        var window = Expression.Parameter(typeof(EditorWindow), "window");
        var call = Expression.Call(Expression.Convert(window, method.DeclaringType!), method);
        return Expression.Lambda<Action<EditorWindow>>(call, window).Compile();
    }

    private readonly record struct WindowMetadata(string? Icon, EditorWindowContextCommand[] Commands);
}
