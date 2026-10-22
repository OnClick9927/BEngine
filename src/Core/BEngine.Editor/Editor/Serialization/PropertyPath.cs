using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class PropertyPath
{
    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, PathSegment[]> ParsedPaths = new(StringComparer.Ordinal);

    public static bool TryResolve(object target, string path, out PropertyAccessor accessor,
        Func<PropertyAccessor, PropertyAccessor>? rootAccessorMap = null)
    {
        try
        {
            accessor = Resolve(target, path, rootAccessorMap);
            return true;
        }
        catch (ArgumentException)
        {
            accessor = default;
            return false;
        }
    }

    public static PropertyAccessor Resolve(object target, string path,
        Func<PropertyAccessor, PropertyAccessor>? rootAccessorMap = null)
    {
        object current = target;
        PropertyAccessor? ownerAccessor = null;
        var segments = GetSegments(path);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var accessor = FindMember(current, segment.MemberName, ownerAccessor);
            if (index == 0 && rootAccessorMap is not null) accessor = rootAccessorMap(accessor);
            if (index == segments.Length - 1 && segment.ItemIndex < 0) return accessor;
            current = accessor.GetValue() ?? throw new ArgumentException(
                $"Property '{segment.MemberName}' is null.", nameof(path));
            if (current is BObject)
                throw new ArgumentException("Object references must be edited through their own SerializedObject.", nameof(path));
            ownerAccessor = accessor;
            if (segment.ItemIndex < 0) continue;
            var itemIndex = segment.ItemIndex;
            if (current is not IList list || itemIndex < 0 || itemIndex >= list.Count)
                throw new ArgumentException($"Array index is outside '{path}'.", nameof(path));
            var listAccessor = new PropertyAccessor(list, itemIndex, accessor.CanWrite, () => accessor.SetValue(list));
            if (index == segments.Length - 1) return listAccessor;
            current = listAccessor.GetValue() ?? throw new ArgumentException($"Array item in '{path}' is null.", nameof(path));
            if (current is BObject)
                throw new ArgumentException("Object references must be edited through their own SerializedObject.", nameof(path));
            ownerAccessor = listAccessor;
        }
        throw new ArgumentException($"Property '{path}' was not found.", nameof(path));
    }

    public static string LeafName(string path)
    {
        var leaf = path[(path.LastIndexOf('.') + 1)..];
        var bracket = leaf.IndexOf('[');
        return bracket < 0 ? leaf : leaf[..bracket];
    }

    internal static string Normalize(string path) => path.Replace(".Array.data[", "[", StringComparison.Ordinal);

    internal static string ParentPath(string path)
    {
        path = Normalize(path);
        var separator = Math.Max(path.LastIndexOf('.'), path.LastIndexOf('['));
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static PropertyAccessor FindMember(object owner, string name, PropertyAccessor? ownerAccessor)
    {
        if (RuntimeTypeCache.TryFindInstanceMember(owner.GetType(), name, out var member))
        {
            // A getter may return a defensive copy (for example curve key arrays).
            // Propagate edits through every owner, including collection properties and
            // reference-valued properties, as well as boxed structs.
            if (ownerAccessor is { } parent)
                return new PropertyAccessor(owner, member, () => parent.SetValue(owner), parent.CanWrite);
            return new PropertyAccessor(owner, member, ownerWritable: ownerAccessor?.CanWrite ?? true);
        }
        throw new ArgumentException($"Property '{name}' was not found on {owner.GetType().Name}.");
    }

    private static PathSegment[] GetSegments(string path)
    {
        lock (CacheGate)
        {
            if (ParsedPaths.TryGetValue(path, out var cached)) return cached;
            cached = path.Replace(".Array.data[", "[")
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment =>
                {
                    var bracket = segment.IndexOf('[');
                    if (bracket < 0) return new PathSegment(segment, -1);
                    var end = segment.IndexOf(']', bracket + 1);
                    if (end < 0 || !int.TryParse(segment[(bracket + 1)..end], out var itemIndex))
                        throw new ArgumentException($"Invalid array property path '{path}'.", nameof(path));
                    return new PathSegment(segment[..bracket], itemIndex);
                }).ToArray();
            if (ParsedPaths.Count < 4096) ParsedPaths[path] = cached;
            return cached;
        }
    }

    private readonly record struct PathSegment(string MemberName, int ItemIndex);
}
