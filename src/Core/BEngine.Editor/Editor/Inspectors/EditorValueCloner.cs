using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class EditorValueCloner
{
    internal static object? CloneValue(object? value) =>
        CloneValue(value, static item => item,
            new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    internal static object? CloneValue(
        object? value,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        ArgumentNullException.ThrowIfNull(objectMapper);
        ArgumentNullException.ThrowIfNull(visited);
        return CloneValueCore(value, objectMapper, visited);
    }

    private static object? CloneValueCore(
        object? value,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        if (value is null || value is string || value is Type)
            return value;
        if (value is Delegate) return null;
        if (value is BObject engineObject) return objectMapper(engineObject);
        var type = value.GetType();
        if (type.IsValueType)
            return ValueTypeContainsReferences(type)
                ? CloneValueType(value, type, objectMapper, visited)
                : value;
        if (visited.TryGetValue(value, out var existing)) return existing;
        if (value is Array array) return CloneArray(array, objectMapper, visited);
        if (value is IDictionary dictionary) return CloneDictionary(dictionary, type, objectMapper, visited);
        if (value is IList list) return CloneList(list, type, objectMapper, visited);

        var clone = CreateInstance(type);
        visited[value] = clone;
        foreach (var field in SerializableFields(type))
            field.SetValue(clone, CloneValueCore(field.GetValue(value), objectMapper, visited));
        return clone;
    }

    private static object CloneValueType(
        object source,
        Type type,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        if (visited.TryGetValue(source, out var existing)) return existing;
        var clone = Activator.CreateInstance(type) ?? RuntimeHelpers.GetUninitializedObject(type);
        visited[source] = clone;
        foreach (var field in InstanceFields(type))
        {
            if (field.IsStatic || field.IsDefined(typeof(NonSerializedAttribute), true)) continue;
            field.SetValue(clone, CloneValueCore(field.GetValue(source), objectMapper, visited));
        }
        return clone;
    }

    private static bool ValueTypeContainsReferences(Type type)
    {
        if (!type.IsValueType) return true;
        if (type.IsPrimitive || type.IsEnum || type.IsPointer) return false;
        foreach (var field in InstanceFields(type))
            if (!field.IsStatic && ValueTypeContainsReferences(field.FieldType))
                return true;
        return false;
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return field;
    }

    private static Array CloneArray(
        Array source,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
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
                else clone.SetValue(CloneValueCore(source.GetValue(indices), objectMapper, visited), indices);
            }
        }
    }

    private static object CloneList(
        IList source,
        Type type,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        if (CreateInstance(type) is not IList clone || clone.IsReadOnly || clone.IsFixedSize)
            throw new NotSupportedException($"List type '{type.FullName}' cannot be cloned for Undo.");
        visited[source] = clone;
        foreach (var item in source) clone.Add(CloneValueCore(item, objectMapper, visited));
        return clone;
    }

    private static object CloneDictionary(
        IDictionary source,
        Type type,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        if (CreateInstance(type) is not IDictionary clone || clone.IsReadOnly || clone.IsFixedSize)
            throw new NotSupportedException($"Dictionary type '{type.FullName}' cannot be cloned for Undo.");
        visited[source] = clone;
        foreach (DictionaryEntry entry in source)
        {
            var key = CloneValueCore(entry.Key, objectMapper, visited) ??
                      throw new InvalidDataException($"Dictionary '{type.FullName}' contains a null key.");
            clone.Add(key, CloneValueCore(entry.Value, objectMapper, visited));
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
            if (field.IsStatic || field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), true) ||
                typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
            if (field.IsPublic || field.IsDefined(typeof(SerializeFieldAttribute), true) ||
                field.IsDefined(typeof(SerializeReferenceAttribute), true)) yield return field;
        }
    }
}
