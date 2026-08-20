using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class EditorValueCloner
{
    internal static object? CloneValue(object? value) =>
        CloneValue(value, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    private static object? CloneValue(object? value, Dictionary<object, object> visited)
    {
        if (value is null || value is BObject || value is string || value is Type || value is Delegate)
            return value;
        var type = value.GetType();
        if (type.IsValueType) return value;
        if (visited.TryGetValue(value, out var existing)) return existing;
        if (value is Array array) return CloneArray(array, visited);
        if (value is IDictionary dictionary) return CloneDictionary(dictionary, type, visited);
        if (value is IList list) return CloneList(list, type, visited);

        var clone = CreateInstance(type);
        visited[value] = clone;
        foreach (var field in SerializableFields(type))
            field.SetValue(clone, CloneValue(field.GetValue(value), visited));
        return clone;
    }

    private static Array CloneArray(Array source, Dictionary<object, object> visited)
    {
        var lengths = Enumerable.Range(0, source.Rank).Select(source.GetLength).ToArray();
        var lowerBounds = Enumerable.Range(0, source.Rank).Select(source.GetLowerBound).ToArray();
        var clone = Array.CreateInstance(source.GetType().GetElementType()!, lengths, lowerBounds);
        visited[source] = clone;
        var indices = new int[source.Rank];
        CopyDimension(0);
        return clone;

        void CopyDimension(int dimension)
        {
            var lower = source.GetLowerBound(dimension);
            var upper = source.GetUpperBound(dimension);
            for (var index = lower; index <= upper; index++)
            {
                indices[dimension] = index;
                if (dimension + 1 < source.Rank) CopyDimension(dimension + 1);
                else clone.SetValue(CloneValue(source.GetValue(indices), visited), indices);
            }
        }
    }

    private static object CloneList(IList source, Type type, Dictionary<object, object> visited)
    {
        if (CreateInstance(type) is not IList clone || clone.IsReadOnly || clone.IsFixedSize)
            throw new NotSupportedException($"List type '{type.FullName}' cannot be cloned for Undo.");
        visited[source] = clone;
        foreach (var item in source) clone.Add(CloneValue(item, visited));
        return clone;
    }

    private static object CloneDictionary(IDictionary source, Type type, Dictionary<object, object> visited)
    {
        if (CreateInstance(type) is not IDictionary clone || clone.IsReadOnly || clone.IsFixedSize)
            throw new NotSupportedException($"Dictionary type '{type.FullName}' cannot be cloned for Undo.");
        visited[source] = clone;
        foreach (DictionaryEntry entry in source)
        {
            var key = CloneValue(entry.Key, visited) ??
                      throw new InvalidDataException($"Dictionary '{type.FullName}' contains a null key.");
            clone.Add(key, CloneValue(entry.Value, visited));
        }
        return clone;
    }

    private static object CreateInstance(Type type)
    {
        try { return Activator.CreateInstance(type, nonPublic: true) ?? RuntimeHelpers.GetUninitializedObject(type); }
        catch (MissingMethodException) { return RuntimeHelpers.GetUninitializedObject(type); }
    }

    private static IEnumerable<FieldInfo> SerializableFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (field.IsStatic || field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), true)) continue;
            if (field.IsPublic || field.IsDefined(typeof(SerializeFieldAttribute), true)) yield return field;
        }
    }
}
