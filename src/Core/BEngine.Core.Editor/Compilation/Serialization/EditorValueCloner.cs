using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

/// <summary>Managed editor cloning: buffer copies borrow engine objects; mapped copies can rebind them.</summary>
public static class EditorValueCloner
{
    private static readonly MethodInfo ShallowClone = typeof(object).GetMethod("MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly ConditionalWeakTable<Type, FieldInfo[]> InstanceFieldCache = new();
    private static readonly ConditionalWeakTable<Type, ReferenceLayout> ReferenceLayouts = new();

    // Inspector buffers must preserve private/readonly state and must not invoke unrelated
    // user getters or setters while editing one healthy member.
    public static object? CloneSerializedBufferValue(object? value)
    {
        if (value is null or string or Type or Delegate or BObject) return value;
        var type = value.GetType();
        if (IsImmutableScalar(type) || type.IsValueType && !ValueTypeContainsReferences(type)) return value;
        return CloneSerializedBufferValue(value, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));
    }

    public static object? CloneSerializedBufferValue(object? value, Dictionary<object, object> visited)
    {
        ArgumentNullException.ThrowIfNull(visited);
        if (value is null or string or Type or Delegate or BObject) return value;
        var type = value.GetType();
        if (IsImmutableScalar(type) || type.IsValueType && !ValueTypeContainsReferences(type)) return value;
        if (visited.TryGetValue(value, out var previous)) return previous;
        if (value is Array array)
        {
            var clone = (Array)array.Clone();
            visited[value] = clone;
            var elementType = type.GetElementType()!;
            if (IsImmutableScalar(elementType) || elementType.IsValueType && !ValueTypeContainsReferences(elementType)) return clone;
            if (array is object?[] items && clone is object?[] clonedItems)
            {
                for (var index = 0; index < items.Length; index++)
                    clonedItems[index] = CloneSerializedBufferValue(items[index], visited);
            }
            else CopySerializedArrayElements(array, clone, visited);
            return clone;
        }
        if (value is IDictionary dictionary && CreateInstance(type) is IDictionary clonedDictionary)
        {
            visited[value] = clonedDictionary;
            foreach (DictionaryEntry item in dictionary)
                clonedDictionary.Add(CloneSerializedBufferValue(item.Key, visited)!,
                    CloneSerializedBufferValue(item.Value, visited));
            return clonedDictionary;
        }
        if (value is IList list && CreateInstance(type) is IList clonedList && !clonedList.IsReadOnly && !clonedList.IsFixedSize)
        {
            visited[value] = clonedList;
            foreach (var item in list) clonedList.Add(CloneSerializedBufferValue(item, visited));
            return clonedList;
        }
        // Framework services/handles are opaque runtime state, not editable serialized graphs.
        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) return value;
        var copy = ShallowClone.Invoke(value, null)!;
        visited[value] = copy;
        foreach (var field in InstanceFields(type))
        {
            if (field.IsStatic || field.IsDefined(typeof(NonSerializedAttribute), true) ||
                field.IsDefined(typeof(YamlDotNet.Serialization.YamlIgnoreAttribute), true)) continue;
            field.SetValue(copy, CloneSerializedBufferValue(field.GetValue(value), visited));
        }
        return copy;
    }

    private static void CopySerializedArrayElements(Array source, Array clone, Dictionary<object, object> visited)
    {
        var indices = new int[source.Rank];
        CopyDimension(0);

        void CopyDimension(int dimension)
        {
            for (var index = source.GetLowerBound(dimension); index <= source.GetUpperBound(dimension); index++)
            {
                indices[dimension] = index;
                if (dimension + 1 < source.Rank) CopyDimension(dimension + 1);
                else clone.SetValue(CloneSerializedBufferValue(source.GetValue(indices), visited), indices);
            }
        }
    }

    public static object? CloneValue(object? value)
    {
        if (value is null or string or Type or BObject) return value;
        if (value is Delegate) return null;
        if (value.GetType() is { IsValueType: true } type && !ValueTypeContainsReferences(type)) return value;
        return CloneValue(value, static item => item,
            new Dictionary<object, object>(ReferenceEqualityComparer.Instance));
    }

    public static object? CloneValue(
        object? value,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        ArgumentNullException.ThrowIfNull(objectMapper);
        ArgumentNullException.ThrowIfNull(visited);
        return CloneValueCore(value, objectMapper, visited);
    }

    public static object? CloneValue(
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
                    $"Mapped object '{mapped.GetType().FullName}' is not assignable to '{destinationType.FullName}'. " +
                    $"Source: {DescribeType(value.GetType())}; mapped: {DescribeType(mapped.GetType())}; " +
                    $"destination: {DescribeType(destinationType)}; active: " +
                    DescribeType(RuntimeTypeCache.FindType(destinationType.FullName ?? string.Empty)));
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
        var sourceMembers = SerializableMembers(sourceType);
        var destinationMembers = SerializableMembers(destinationType);
        if (sourceMembers is null || destinationMembers is null) return clone;
        foreach (var destinationMember in destinationMembers.Values)
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

    public static string DescribeType(Type? type) => type is null ? "<none>" :
        $"{type.AssemblyQualifiedName} [assemblyIdentity={RuntimeHelpers.GetHashCode(type.Assembly):x}, " +
        $"mvid={type.Module.ModuleVersionId}]";

    private static Array CloneArrayToType(
        Array source,
        Type destinationType,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        var elementType = destinationType.GetElementType() ??
                          throw new InvalidDataException($"'{destinationType.FullName}' is not an array type.");
        var lengths = new int[source.Rank];
        var lowerBounds = new int[source.Rank];
        for (var dimension = 0; dimension < source.Rank; dimension++)
        {
            lengths[dimension] = source.GetLength(dimension);
            lowerBounds[dimension] = source.GetLowerBound(dimension);
        }
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
            var arguments = (sourceType.GetGenericArguments() is { } projectionSource13639 ? projectionSource13639.Length == 0 ? global::System.Array.Empty<global::System.Type>() : global::System.Array.ConvertAll<global::System.Type, global::System.Type>(projectionSource13639, ResolvePreferredType) : throw new global::System.ArgumentNullException("source"));
            try { return preferredDefinition.MakeGenericType(arguments); }
            catch (ArgumentException) { return sourceType; }
        }
        return RuntimeTypeCache.ResolveSerializedType(
            sourceType.AssemblyQualifiedName ?? sourceType.FullName ?? string.Empty) ?? sourceType;
    }

    private static Type[] GetCollectionArguments(Type type, Type genericInterface)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface)
            return type.GetGenericArguments();
        foreach (var candidate in type.GetInterfaces())
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericInterface)
                return candidate.GetGenericArguments();
        return [];
    }

    private static Dictionary<string, MemberInfo>? SerializableMembers(Type type)
    {
        Dictionary<string, MemberInfo>? members = null;
        foreach (var member in RuntimeTypeCache.GetInstanceMembers(type))
        {
            // Evaluate metadata before deduplicating, including hidden base members.
            // Keep this map local: providers may change their filtering after a reload.
            if (RuntimeTypeCache.IsSerializableMember(member) &&
                !member.IsDefined(typeof(YamlDotNet.Serialization.YamlIgnoreAttribute), true))
                (members ??= new(StringComparer.Ordinal)).TryAdd(member.Name, member);
        }
        return members;
    }

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
        var members = SerializableMembers(type);
        if (members is null) return clone;
        foreach (var member in members.Values)
        {
            var accessor = RuntimeTypeCache.GetMemberAccessor(member);
            accessor.Setter?.Invoke(clone, CloneValueCore(accessor.Getter(value), objectMapper, visited));
        }
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
        return ReferenceLayouts.GetValue(type, static key =>
        {
            foreach (var field in InstanceFields(key))
                if (!field.IsStatic && ValueTypeContainsReferences(field.FieldType)) return new(true);
            return new(false);
        }).ContainsReferences;
    }

    private sealed record ReferenceLayout(bool ContainsReferences);

    internal static ReadOnlySpan<FieldInfo> InstanceFieldsSpan(Type type) => InstanceFields(type);

    private static FieldInfo[] InstanceFields(Type type) => InstanceFieldCache.GetValue(type, static key =>
    {
        var fields = new List<FieldInfo>();
        for (var current = key; current is not null && current != typeof(object); current = current.BaseType)
            fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        return fields.ToArray();
    });

    private static Array CloneArray(
        Array source,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        var type = source.GetType();
        var elementType = type.GetElementType()!;
        if (type.IsSZArray &&
            (source.Length == 0 || IsImmutableScalar(elementType) ||
             elementType.IsValueType && !ValueTypeContainsReferences(elementType)))
        {
            var clone = (Array)source.Clone();
            visited[source] = clone;
            return clone;
        }
        return CloneArrayByElement(source, objectMapper, visited);
    }

    private static Array CloneArrayByElement(
        Array source,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        var lengths = new int[source.Rank];
        var lowerBounds = new int[source.Rank];
        for (var dimension = 0; dimension < source.Rank; dimension++)
        {
            lengths[dimension] = source.GetLength(dimension);
            lowerBounds[dimension] = source.GetLowerBound(dimension);
        }
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
}
