using System.Reflection;

namespace BEngine.UIElements;

public sealed class UIBinding : IDisposable
{
    private static readonly Dictionary<(Type Type, string Path), MemberInfo[]> PathCache = [];
    private readonly object _source;
    private readonly List<Entry> _entries = [];
    private bool _disposed;

    internal UIBinding(VisualElement root, object source)
    {
        _source = source;
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element is not IBindableField field || string.IsNullOrWhiteSpace(element.bindingPath)) continue;
            var members = ResolvePath(source.GetType(), element.bindingPath);
            Action<object?> changed = value => WritePath(_source, members, value, field.ValueType);
            field.BoxedValueChanged += changed;
            _entries.Add(new Entry(field, members, changed));
        }
        Refresh();
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var entry in _entries)
            entry.Field.SetBoxedValueWithoutNotify(ReadPath(_source, entry.Members));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries) entry.Field.BoxedValueChanged -= entry.Changed;
        _entries.Clear();
    }

    private static MemberInfo[] ResolvePath(Type sourceType, string path)
    {
        var key = (sourceType, path);
        if (PathCache.TryGetValue(key, out var cached)) return cached;
        var current = sourceType;
        var members = new List<MemberInfo>();
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var member = (MemberInfo?)current.GetProperty(segment,
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                         current.GetField(segment,
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                         throw new InvalidOperationException(
                             $"Binding path '{path}' was not found on '{sourceType.FullName}'.");
            current = MemberType(member);
            members.Add(member);
        }
        if (members.Count == 0) throw new ArgumentException("Binding path is empty.", nameof(path));
        return PathCache[key] = members.ToArray();
    }

    private static object? ReadPath(object source, IReadOnlyList<MemberInfo> members)
    {
        object? current = source;
        foreach (var member in members)
        {
            if (current is null) return null;
            current = member switch
            {
                PropertyInfo property => property.GetValue(current),
                FieldInfo field => field.GetValue(current),
                _ => null
            };
        }
        return current;
    }

    private static void WritePath(
        object source,
        IReadOnlyList<MemberInfo> members,
        object? value,
        Type sourceValueType)
    {
        object? owner = source;
        for (var index = 0; index < members.Count - 1; index++)
        {
            owner = members[index] switch
            {
                PropertyInfo property => property.GetValue(owner),
                FieldInfo field => field.GetValue(owner),
                _ => null
            };
            if (owner is null) return;
        }
        var target = members[^1];
        var targetType = MemberType(target);
        var converted = ConvertValue(value, sourceValueType, targetType);
        switch (target)
        {
            case PropertyInfo { CanWrite: true } property: property.SetValue(owner, converted); break;
            case FieldInfo field: field.SetValue(owner, converted); break;
        }
    }

    private static object? ConvertValue(object? value, Type sourceType, Type targetType)
    {
        if (value is null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        if (targetType.IsInstanceOfType(value)) return value;
        if (targetType.IsEnum)
            return value is string text ? Enum.Parse(targetType, text, ignoreCase: true) :
                Enum.ToObject(targetType, value);
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new NotSupportedException($"Unsupported binding member '{member.MemberType}'.")
    };

    private sealed record Entry(IBindableField Field, MemberInfo[] Members, Action<object?> Changed);
}
