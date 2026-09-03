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

    internal static object? CloneValue(
        object? value,
        Type destinationType,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        ArgumentNullException.ThrowIfNull(objectMapper);
        ArgumentNullException.ThrowIfNull(visited);
        return CloneValueToType(value, destinationType, objectMapper, visited);
    }

    private static object? CloneValueToType(
        object? value,
        Type destinationType,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        if (value is null)
        {
            if (destinationType.IsValueType && Nullable.GetUnderlyingType(destinationType) is null)
                throw new InvalidDataException($"Cannot assign null to '{destinationType.FullName}'.");
            return null;
        }
        if (value is BObject engineObject)
        {
            var mapped = objectMapper(engineObject);
            if (destinationType != typeof(object) && !destinationType.IsInstanceOfType(mapped))
                throw new InvalidDataException(
                    $"Mapped object '{mapped.GetType().FullName}' is not assignable to '{destinationType.FullName}'.");
            return mapped;
        }
        if (value is Delegate) return null;

        var sourceType = value.GetType();
        destinationType = ResolveDestinationType(sourceType, destinationType);
        if (value is Type sourceRuntimeType)
            return ResolvePreferredType(sourceRuntimeType);
        if (sourceType == destinationType)
            return CloneValueCore(value, objectMapper, visited);
        if (destinationType.IsEnum)
            return Enum.Parse(destinationType, value.ToString() ?? string.Empty, ignoreCase: false);
        if (IsImmutableScalar(sourceType) && destinationType.IsAssignableFrom(sourceType)) return value;
        if (visited.TryGetValue(value, out var existing)) return existing;
        if (value is Array array)
            return CloneArrayToType(array, destinationType, objectMapper, visited);
        if (value is IDictionary dictionary)
            return CloneDictionaryToType(dictionary, destinationType, objectMapper, visited);
        if (value is IList list)
            return CloneListToType(list, destinationType, objectMapper, visited);

        var clone = CreateInstance(destinationType);
        visited[value] = clone;
        var sourceMembers = SerializableMembers(sourceType)
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var destinationMember in SerializableMembers(destinationType))
        {
            if (!sourceMembers.TryGetValue(destinationMember.Name, out var sourceMember)) continue;
            var destinationAccessor = RuntimeTypeCache.GetMemberAccessor(destinationMember);
            if (destinationAccessor.Setter is null) continue;
            var sourceAccessor = RuntimeTypeCache.GetMemberAccessor(sourceMember);
            var memberValue = CloneValueToType(
                sourceAccessor.Getter(value), destinationAccessor.ValueType, objectMapper, visited);
            destinationAccessor.Setter(clone, memberValue);
        }
        return clone;
    }

    private static Array CloneArrayToType(
        Array source,
        Type destinationType,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        var elementType = destinationType.GetElementType() ??
                          throw new InvalidDataException($"'{destinationType.FullName}' is not an array type.");
        var lengths = Enumerable.Range(0, source.Rank).Select(source.GetLength).ToArray();
        var lowerBounds = Enumerable.Range(0, source.Rank).Select(source.GetLowerBound).ToArray();
        var clone = Array.CreateInstance(elementType, lengths, lowerBounds);
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
                else clone.SetValue(CloneValueToType(
                    source.GetValue(indices), elementType, objectMapper, visited), indices);
            }
        }
    }

    private static object CloneListToType(
        IList source,
        Type destinationType,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        if (CreateInstance(destinationType) is not IList clone || clone.IsReadOnly || clone.IsFixedSize)
            throw new NotSupportedException($"List type '{destinationType.FullName}' cannot be cloned for Play Mode.");
        visited[source] = clone;
        var elementType = GetCollectionArguments(destinationType, typeof(IList<>)).FirstOrDefault() ?? typeof(object);
        foreach (var item in source)
            clone.Add(CloneValueToType(item, elementType, objectMapper, visited));
        return clone;
    }

    private static object CloneDictionaryToType(
        IDictionary source,
        Type destinationType,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        if (CreateInstance(destinationType) is not IDictionary clone || clone.IsReadOnly || clone.IsFixedSize)
            throw new NotSupportedException(
                $"Dictionary type '{destinationType.FullName}' cannot be cloned for Play Mode.");
        visited[source] = clone;
        var arguments = GetCollectionArguments(destinationType, typeof(IDictionary<,>));
        var keyType = arguments.ElementAtOrDefault(0) ?? typeof(object);
        var valueType = arguments.ElementAtOrDefault(1) ?? typeof(object);
        foreach (DictionaryEntry entry in source)
        {
            var key = CloneValueToType(entry.Key, keyType, objectMapper, visited) ??
                      throw new InvalidDataException(
                          $"Dictionary '{destinationType.FullName}' contains a null key.");
            clone.Add(key, CloneValueToType(entry.Value, valueType, objectMapper, visited));
        }
        return clone;
    }

    private static Type ResolveDestinationType(Type sourceType, Type destinationType)
    {
        var nullable = Nullable.GetUnderlyingType(destinationType);
        if (nullable is not null) destinationType = nullable;
        if (destinationType != typeof(object) && !destinationType.IsInterface && !destinationType.IsAbstract)
            return destinationType;
        var preferred = ResolvePreferredType(sourceType);
        return destinationType == typeof(object) || destinationType.IsAssignableFrom(preferred)
            ? preferred
            : destinationType;
    }

    private static Type ResolvePreferredType(Type sourceType)
    {
        if (sourceType.IsArray)
        {
            var elementType = ResolvePreferredType(sourceType.GetElementType()!);
            return sourceType.GetArrayRank() == 1 ? elementType.MakeArrayType() :
                elementType.MakeArrayType(sourceType.GetArrayRank());
        }
        if (sourceType.IsGenericType)
        {
            var definition = sourceType.GetGenericTypeDefinition();
            var preferredDefinition = RuntimeTypeCache.FindType(definition.FullName ?? string.Empty) ?? definition;
            var arguments = sourceType.GetGenericArguments().Select(ResolvePreferredType).ToArray();
            try { return preferredDefinition.MakeGenericType(arguments); }
            catch (ArgumentException) { return sourceType; }
        }
        return RuntimeTypeCache.FindType(sourceType.AssemblyQualifiedName ?? string.Empty) ??
               RuntimeTypeCache.FindType(sourceType.FullName ?? string.Empty) ?? sourceType;
    }

    private static Type[] GetCollectionArguments(Type type, Type genericInterface)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface)
            return type.GetGenericArguments();
        return type.GetInterfaces().FirstOrDefault(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericInterface)
            ?.GetGenericArguments() ?? [];
    }

    private static IEnumerable<MemberInfo> SerializableMembers(Type type) =>
        RuntimeTypeCache.GetInstanceMembers(type)
            .Where(RuntimeTypeCache.IsSerializableMember)
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .Select(group => group.First());

    private static bool IsImmutableScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
        type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan);

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
            if (RuntimeTypeCache.IsSerializableMember(field)) yield return field;
        }
    }
}
