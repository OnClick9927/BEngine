using System.Reflection;

namespace BEngine.Editor;

internal static class CreateAssetMenuRegistry
{
    private static int _generation = -1;
    private static CreateAssetMenuEntry[] _entries = [];

    internal static IReadOnlyList<CreateAssetMenuEntry> entries
    {
        get
        {
            EnsureFresh();
            return _entries;
        }
    }

    internal static void Warmup() => EnsureFresh();

    internal static Type? FindAssetType(string displayName)
    {
        EnsureFresh();
        return _entries.Select(entry => entry.AssetType).FirstOrDefault(type =>
            type.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase) ||
            ObjectNames.NicifyVariableName(type.Name).Equals(displayName, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        if (_generation == generation) return;
        _entries = TypeCache.GetTypesWithAttribute<CreateAssetMenuAttribute>()
            .Where(type => !type.IsAbstract && typeof(BObject).IsAssignableFrom(type))
            .Select(ReadAttribute)
            .Where(item => item.Attribute is not null && !string.IsNullOrWhiteSpace(item.Attribute.menuName))
            .Select(item => new CreateAssetMenuEntry(item.Type, item.Attribute!.menuName.Trim('/'),
                string.IsNullOrWhiteSpace(item.Attribute.fileName) ? $"New {item.Type.Name}" :
                item.Attribute.fileName.Trim(), item.Attribute.order))
            .OrderBy(entry => entry.Order)
            .ThenBy(entry => entry.MenuName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _generation = generation;
    }

    private static (Type Type, CreateAssetMenuAttribute? Attribute) ReadAttribute(Type type)
    {
        EditorFeatureGuard.TryInvoke($"CreateAssetMenu attribute {type.FullName}",
            () => type.GetCustomAttribute<CreateAssetMenuAttribute>(false), null, out var attribute);
        return (type, attribute);
    }
}
