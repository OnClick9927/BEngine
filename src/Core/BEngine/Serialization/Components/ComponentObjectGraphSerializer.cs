using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Serialization;

internal static class ComponentObjectGraphSerializer
{
    private enum ValueKind
    {
        NullValue,
        Scalar,
        Enum,
        EngineObject,
        Array,
        List,
        Dictionary,
        Object,
        Reference
    }

    internal static ComponentGraphData Capture(
        Component component,
        IEnumerable<MemberInfo>? members = null,
        bool allowTransientObjects = false)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component is MissingComponent missing && missing.serializedGraph is { } missingGraph)
            return Clone(missingGraph);

        var context = new WriteContext(allowTransientObjects);
        return new ComponentGraphData
        {
            Members = [.. (members ?? ComponentFieldSerializer.GetSerializableMembers(component.GetType()))
                .Select(member => new SerializedMemberData
                {
                    Name = member.Name,
                    Value = context.Write(ComponentFieldSerializer.GetMemberValue(member, component),
                        ComponentFieldSerializer.GetMemberType(member), member.Name)
                })]
        };
    }

    internal static void Restore(
        Component component,
        ComponentGraphData graph,
        Func<Guid, BObject?>? sceneObjectResolver = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.Version != 1)
            throw new InvalidDataException($"Unsupported component object graph version {graph.Version}.");
        if (component is MissingComponent missing)
        {
            missing.serializedGraph = Clone(graph);
            return;
        }

        var context = new ReadContext(sceneObjectResolver);
        var members = ComponentFieldSerializer.GetSerializableMembers(component.GetType())
            .ToDictionary(member => member.Name, StringComparer.Ordinal);
        foreach (var serializedMember in graph.Members)
        {
            var member = ResolveMember(serializedMember.Name, members.Values);
            if (member is null) continue;
            var memberType = ComponentFieldSerializer.GetMemberType(member);
            var value = context.Read(serializedMember.Value, memberType, serializedMember.Name);
            ComponentFieldSerializer.SetMemberValue(member, component, value);
        }
    }

    internal static ComponentGraphData Clone(ComponentGraphData graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new ComponentGraphData
        {
            Version = graph.Version,
            Members = [.. graph.Members.Select(Clone)]
        };
    }

    private static SerializedMemberData Clone(SerializedMemberData member) => new()
    {
        Name = member.Name,
        Value = Clone(member.Value)
    };

    private static SerializedValueData Clone(SerializedValueData value) => new()
    {
        Kind = value.Kind,
        Type = value.Type,
        Value = value.Value,
        ObjectId = value.ObjectId,
        ReferenceId = value.ReferenceId,
        Dimensions = [.. value.Dimensions],
        Items = [.. value.Items.Select(Clone)],
        Members = [.. value.Members.Select(Clone)],
        Entries = [.. value.Entries.Select(entry => new SerializedDictionaryEntryData
        {
            Key = Clone(entry.Key),
            Value = Clone(entry.Value)
        })]
    };

    private static MemberInfo? ResolveMember(string name, IEnumerable<MemberInfo> members)
    {
        foreach (var member in members)
        {
            if (member.Name.Equals(name, StringComparison.Ordinal)) return member;
            if (member.GetCustomAttributes<FormerlySerializedAsAttribute>(inherit: true)
                .Any(attribute => attribute.oldName.Equals(name, StringComparison.Ordinal))) return member;
        }
        return null;
    }

    private static IReadOnlyList<MemberInfo> GetNestedMembers(Type type) =>
        RuntimeTypeCache.GetInstanceMembers(type)
            .Where(RuntimeTypeCache.IsSerializableMember)
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();

    private static string TypeName(Type type) =>
        type.AssemblyQualifiedName ?? type.FullName ?? type.Name;

    private static Type ResolveType(string typeName, Type declaredType, string path)
    {
        var type = string.IsNullOrWhiteSpace(typeName)
            ? declaredType
            : BAssetReferenceLoader.ResolveType(typeName) ?? RuntimeTypeCache.FindType(typeName) ??
              throw new InvalidDataException($"Serialized type '{typeName}' at '{path}' is not loaded.");
        var nullableType = Nullable.GetUnderlyingType(declaredType);
        if (declaredType != typeof(object) && !declaredType.IsAssignableFrom(type) && nullableType != type)
            throw new InvalidDataException(
                $"Serialized type '{type.FullName}' at '{path}' is not assignable to '{declaredType.FullName}'.");
        return type;
    }

    private static bool IsReferenceType(Type type) => !type.IsValueType && type != typeof(string);

    private static bool IsScalar(Type type) => type.IsPrimitive || type == typeof(string) ||
        type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Fix64) ||
        type == typeof(Vector2) || type == typeof(Vector4) || type == typeof(Color) ||
        type == typeof(Rect) || type == typeof(Hash128);

    private sealed class WriteContext(bool allowTransientObjects)
    {
        private readonly Dictionary<object, int> _objects = new(ReferenceEqualityComparer.Instance);
        private int _nextObjectId;

        internal SerializedValueData Write(object? value, Type declaredType, string path)
        {
            if (value is null) return New(ValueKind.NullValue, declaredType);
            var type = value.GetType();
            if (value is BObject engineObject) return WriteEngineObject(engineObject, declaredType, path);
            if (type.IsEnum)
            {
                var data = New(ValueKind.Enum, type);
                data.Value = value.ToString() ?? string.Empty;
                return data;
            }
            if (IsScalar(type))
            {
                var data = New(ValueKind.Scalar, type);
                data.Value = FormatScalar(value, type);
                return data;
            }
            if (value is Type or Delegate)
                throw new NotSupportedException($"Member '{path}' uses unsupported type '{type.FullName}'.");

            if (IsReferenceType(type) && _objects.TryGetValue(value, out var existingId))
            {
                var reference = New(ValueKind.Reference, type);
                reference.ReferenceId = existingId;
                return reference;
            }

            var result = value switch
            {
                Array array => WriteArray(array, type, path),
                IDictionary dictionary => WriteDictionary(dictionary, type, path),
                IList list => WriteList(list, type, path),
                _ when typeof(IEnumerable).IsAssignableFrom(type) =>
                    throw new NotSupportedException(
                        $"Collection member '{path}' uses unsupported collection type '{type.FullName}'. " +
                        "Use an array, IList, or IDictionary."),
                _ => WriteObject(value, type, path)
            };
            return result;
        }

        private SerializedValueData WriteEngineObject(BObject value, Type declaredType, string path)
        {
            var result = New(ValueKind.EngineObject, value.GetType());
            result.Value = value switch
            {
                BAsset asset when asset.parentAssetGuid.HasValue && asset.localIdentifier > 0 =>
                    $"guid:{asset.parentAssetGuid.Value:N}#subasset={asset.localIdentifier.ToString(CultureInfo.InvariantCulture)}",
                Material material when string.IsNullOrWhiteSpace(material.assetPath) &&
                                       material.name.StartsWith("Default ", StringComparison.Ordinal) =>
                    $"builtin-material:{Uri.EscapeDataString(material.shader.shaderName)}|{Uri.EscapeDataString(material.name)}",
                Shader shader when string.IsNullOrWhiteSpace(shader.assetPath) =>
                    $"builtin-shader:{Uri.EscapeDataString(shader.shaderName)}",
                BAsset asset => asset.assetPath,
                Sprite sprite when !string.IsNullOrWhiteSpace(sprite.OwnerGuid) && sprite.LocalIdentifier > 0 =>
                    $"guid:{sprite.OwnerGuid}#subasset={sprite.LocalIdentifier.ToString(CultureInfo.InvariantCulture)}",
                Sprite sprite => sprite.assetPath,
                GameObject or Component => $"scene:{value.Id:D}",
                _ => throw new NotSupportedException(
                    $"BObject member '{path}' uses unsupported runtime type '{value.GetType().FullName}'.")
            };
            if (string.IsNullOrWhiteSpace(result.Value))
            {
                if (allowTransientObjects) result.Value = $"instance:{value.GetInstanceID()}";
                else throw new InvalidDataException(
                    $"BObject member '{path}' references an object without a persistent identity.");
            }
            return result;
        }

        private SerializedValueData WriteArray(Array array, Type type, string path)
        {
            var result = NewTracked(ValueKind.Array, array, type);
            result.Dimensions = [.. Enumerable.Range(0, array.Rank).Select(array.GetLength)];
            var elementType = type.GetElementType()!;
            var index = 0;
            foreach (var item in array)
                result.Items.Add(Write(item, elementType, $"{path}[{index++}]"));
            return result;
        }

        private SerializedValueData WriteList(IList list, Type type, string path)
        {
            var result = NewTracked(ValueKind.List, list, type);
            var elementType = GetListElementType(type);
            for (var index = 0; index < list.Count; index++)
                result.Items.Add(Write(list[index], elementType, $"{path}[{index}]"));
            return result;
        }

        private SerializedValueData WriteDictionary(IDictionary dictionary, Type type, string path)
        {
            var result = NewTracked(ValueKind.Dictionary, dictionary, type);
            var (keyType, valueType) = GetDictionaryTypes(type);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is null)
                    throw new InvalidDataException($"Dictionary member '{path}' contains a null key.");
                result.Entries.Add(new SerializedDictionaryEntryData
                {
                    Key = Write(entry.Key, keyType, $"{path}.Key"),
                    Value = Write(entry.Value, valueType, $"{path}[{entry.Key}]")
                });
            }
            return result;
        }

        private SerializedValueData WriteObject(object value, Type type, string path)
        {
            var result = IsReferenceType(type)
                ? NewTracked(ValueKind.Object, value, type)
                : New(ValueKind.Object, type);
            foreach (var member in GetNestedMembers(type))
            {
                result.Members.Add(new SerializedMemberData
                {
                    Name = member.Name,
                    Value = Write(ComponentFieldSerializer.GetMemberValue(member, value),
                        ComponentFieldSerializer.GetMemberType(member), $"{path}.{member.Name}")
                });
            }
            return result;
        }

        private SerializedValueData NewTracked(ValueKind kind, object value, Type type)
        {
            var result = New(kind, type);
            result.ObjectId = ++_nextObjectId;
            _objects.Add(value, result.ObjectId);
            return result;
        }
    }

    private sealed class ReadContext(Func<Guid, BObject?>? sceneObjectResolver)
    {
        private readonly Dictionary<int, object> _objects = [];

        internal object? Read(SerializedValueData data, Type declaredType, string path)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (!Enum.TryParse<ValueKind>(data.Kind, ignoreCase: false, out var kind))
                throw new InvalidDataException($"Serialized value at '{path}' has unknown kind '{data.Kind}'.");
            if (kind == ValueKind.NullValue)
            {
                if (declaredType.IsValueType && Nullable.GetUnderlyingType(declaredType) is null)
                    throw new InvalidDataException($"Serialized value at '{path}' cannot assign null to {declaredType}.");
                return null;
            }
            if (kind == ValueKind.Reference) return ResolveReference(data.ReferenceId, declaredType, path);

            var type = ResolveType(data.Type, declaredType, path);
            return kind switch
            {
                ValueKind.Scalar => ParseScalar(data.Value, type),
                ValueKind.Enum => Enum.Parse(type, data.Value, ignoreCase: false),
                ValueKind.EngineObject => ReadEngineObject(data.Value, type, declaredType, path),
                ValueKind.Array => ReadArray(data, type, path),
                ValueKind.List => ReadList(data, type, path),
                ValueKind.Dictionary => ReadDictionary(data, type, path),
                ValueKind.Object => ReadObject(data, type, path),
                _ => throw new InvalidDataException($"Serialized value at '{path}' has invalid kind '{kind}'.")
            };
        }

        private object? ReadEngineObject(string reference, Type type, Type declaredType, string path)
        {
            BObject? result;
            if (reference.StartsWith("scene:", StringComparison.Ordinal))
            {
                if (!Guid.TryParse(reference.AsSpan("scene:".Length), out var id))
                    throw new InvalidDataException($"Scene object reference '{reference}' at '{path}' is invalid.");
                result = sceneObjectResolver?.Invoke(id);
            }
            else if (reference.StartsWith("instance:", StringComparison.Ordinal))
            {
                result = int.TryParse(reference.AsSpan("instance:".Length),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var instanceId)
                    ? BObject.FindObjectFromInstanceID(instanceId)
                    : null;
            }
            else if (reference.StartsWith("builtin-shader:", StringComparison.Ordinal) &&
                     typeof(Shader).IsAssignableFrom(type))
                result = Shader.Find(Uri.UnescapeDataString(reference["builtin-shader:".Length..]));
            else if (reference.StartsWith("builtin-material:", StringComparison.Ordinal) &&
                     typeof(Material).IsAssignableFrom(type))
            {
                var parts = reference["builtin-material:".Length..].Split('|', 2);
                if (parts.Length != 2)
                    throw new InvalidDataException($"Built-in Material reference at '{path}' is invalid.");
                result = Material.GetBuiltIn(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
            }
            else result = BAssetReferenceLoader.LoadObjectReference(reference, type);

            if (result is null)
            {
                Debug.LogWarning($"Could not resolve object reference '{reference}' at '{path}'.");
                return null;
            }
            if (!declaredType.IsInstanceOfType(result))
                throw new InvalidDataException(
                    $"Object reference '{reference}' at '{path}' resolved to {result.GetType().FullName}, " +
                    $"expected {declaredType.FullName}.");
            return result;
        }

        private object ReadArray(SerializedValueData data, Type type, string path)
        {
            var elementType = type.GetElementType() ??
                              throw new InvalidDataException($"Serialized array type '{type}' at '{path}' is invalid.");
            var dimensions = data.Dimensions.Count > 0 ? data.Dimensions.ToArray() : [data.Items.Count];
            if (dimensions.Any(length => length < 0) || dimensions.Aggregate(1L, (count, length) => count * length) !=
                data.Items.Count)
                throw new InvalidDataException($"Serialized array dimensions at '{path}' do not match its items.");
            var array = Array.CreateInstance(elementType, dimensions);
            Register(data.ObjectId, array, path);
            for (var flatIndex = 0; flatIndex < data.Items.Count; flatIndex++)
                array.SetValue(Read(data.Items[flatIndex], elementType, $"{path}[{flatIndex}]"),
                    ToIndices(flatIndex, dimensions));
            return array;
        }

        private object ReadList(SerializedValueData data, Type type, string path)
        {
            var list = CreateList(type, path);
            Register(data.ObjectId, list, path);
            var elementType = GetListElementType(type);
            for (var index = 0; index < data.Items.Count; index++)
                list.Add(Read(data.Items[index], elementType, $"{path}[{index}]"));
            return list;
        }

        private object ReadDictionary(SerializedValueData data, Type type, string path)
        {
            var dictionary = CreateDictionary(type, path);
            Register(data.ObjectId, dictionary, path);
            var (keyType, valueType) = GetDictionaryTypes(type);
            foreach (var entry in data.Entries)
            {
                var key = Read(entry.Key, keyType, $"{path}.Key") ??
                          throw new InvalidDataException($"Dictionary member '{path}' contains a null key.");
                dictionary.Add(key, Read(entry.Value, valueType, $"{path}[{key}]"));
            }
            return dictionary;
        }

        private object ReadObject(SerializedValueData data, Type type, string path)
        {
            var instance = CreateObject(type, path);
            if (IsReferenceType(type)) Register(data.ObjectId, instance, path);
            var members = GetNestedMembers(type).ToDictionary(member => member.Name, StringComparer.Ordinal);
            foreach (var serializedMember in data.Members)
            {
                var member = ResolveMember(serializedMember.Name, members.Values);
                if (member is null) continue;
                var memberType = ComponentFieldSerializer.GetMemberType(member);
                ComponentFieldSerializer.SetMemberValue(member, instance,
                    Read(serializedMember.Value, memberType, $"{path}.{member.Name}"));
            }
            return instance;
        }

        private object ResolveReference(int id, Type declaredType, string path)
        {
            if (id <= 0 || !_objects.TryGetValue(id, out var value))
                throw new InvalidDataException($"Serialized reference {id} at '{path}' has no earlier object.");
            if (declaredType != typeof(object) && !declaredType.IsInstanceOfType(value))
                throw new InvalidDataException(
                    $"Serialized reference {id} at '{path}' is not assignable to {declaredType.FullName}.");
            return value;
        }

        private void Register(int id, object value, string path)
        {
            if (id <= 0 || !_objects.TryAdd(id, value))
                throw new InvalidDataException($"Serialized object id {id} at '{path}' is invalid or duplicated.");
        }
    }

    private static SerializedValueData New(ValueKind kind, Type type) => new()
    {
        Kind = kind.ToString(),
        Type = TypeName(type)
    };

    private static string FormatScalar(object value, Type type)
    {
        if (type == typeof(string)) return (string)value;
        if (type == typeof(char)) return ((int)(char)value).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return (bool)value ? bool.TrueString : bool.FalseString;
        if (type == typeof(Guid)) return ((Guid)value).ToString("D");
        if (type == typeof(DateTime)) return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);
        if (type == typeof(DateTimeOffset)) return ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);
        if (type == typeof(TimeSpan)) return ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture);
        if (type == typeof(Fix64)) return ((Fix64)value).ToString();
        if (type == typeof(Vector2)) return Join(((Vector2)value).x, ((Vector2)value).y);
        if (type == typeof(Vector4))
        {
            var vector = (Vector4)value;
            return Join(vector.x, vector.y, vector.z, vector.w);
        }
        if (type == typeof(Color))
        {
            var color = (Color)value;
            return Join(color.r, color.g, color.b, color.a);
        }
        if (type == typeof(Rect))
        {
            var rect = (Rect)value;
            return Join(rect.x, rect.y, rect.width, rect.height);
        }
        if (type == typeof(Hash128)) return value.ToString()!;
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static object ParseScalar(string value, Type type)
    {
        if (type == typeof(string)) return value;
        if (type == typeof(char)) return (char)int.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(value);
        if (type == typeof(byte)) return byte.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(sbyte)) return sbyte.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(short)) return short.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(ushort)) return ushort.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(uint)) return uint.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(long)) return long.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(ulong)) return ulong.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(decimal)) return decimal.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(Guid)) return Guid.Parse(value);
        if (type == typeof(DateTime)) return DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        if (type == typeof(TimeSpan)) return TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture);
        if (type == typeof(Fix64)) return Fix64.Parse(value);
        if (type == typeof(Vector2))
        {
            var parts = Split(value, 2);
            return new Vector2(Fix64.Parse(parts[0]), Fix64.Parse(parts[1]));
        }
        if (type == typeof(Vector4))
        {
            var parts = Split(value, 4);
            return new Vector4(Fix64.Parse(parts[0]), Fix64.Parse(parts[1]), Fix64.Parse(parts[2]),
                Fix64.Parse(parts[3]));
        }
        if (type == typeof(Color))
        {
            var parts = Split(value, 4);
            return new Color(Fix64.Parse(parts[0]), Fix64.Parse(parts[1]), Fix64.Parse(parts[2]),
                Fix64.Parse(parts[3]));
        }
        if (type == typeof(Rect))
        {
            var parts = Split(value, 4);
            return new Rect(Fix64.Parse(parts[0]), Fix64.Parse(parts[1]), Fix64.Parse(parts[2]),
                Fix64.Parse(parts[3]));
        }
        if (type == typeof(Hash128)) return Hash128.Parse(value);
        throw new NotSupportedException($"Scalar type '{type.FullName}' is not supported.");
    }

    private static string Join(params Fix64[] values) =>
        string.Join(',', values.Select(value => value.ToString()));

    private static string[] Split(string value, int count)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == count
            ? parts
            : throw new FormatException($"Expected {count} comma-separated values.");
    }

    private static Type GetListElementType(Type type) => type.IsArray
        ? type.GetElementType()!
        : type.GetInterfaces().Append(type)
              .FirstOrDefault(candidate => candidate.IsGenericType &&
                                           candidate.GetGenericTypeDefinition() == typeof(IList<>))
              ?.GetGenericArguments()[0] ?? typeof(object);

    private static (Type Key, Type Value) GetDictionaryTypes(Type type)
    {
        var dictionaryType = type.GetInterfaces().Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType &&
                                         candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        return dictionaryType is null
            ? (typeof(object), typeof(object))
            : (dictionaryType.GetGenericArguments()[0], dictionaryType.GetGenericArguments()[1]);
    }

    private static IList CreateList(Type type, string path)
    {
        object? instance = null;
        if (!type.IsAbstract && !type.IsInterface) instance = CreateObject(type, path);
        else instance = Activator.CreateInstance(typeof(List<>).MakeGenericType(GetListElementType(type)));
        return instance as IList is { IsReadOnly: false, IsFixedSize: false } list
            ? list
            : throw new NotSupportedException($"List type '{type.FullName}' at '{path}' is not mutable.");
    }

    private static IDictionary CreateDictionary(Type type, string path)
    {
        object? instance = null;
        if (!type.IsAbstract && !type.IsInterface) instance = CreateObject(type, path);
        else
        {
            var (keyType, valueType) = GetDictionaryTypes(type);
            instance = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
        }
        return instance as IDictionary is { IsReadOnly: false, IsFixedSize: false } dictionary
            ? dictionary
            : throw new NotSupportedException($"Dictionary type '{type.FullName}' at '{path}' is not mutable.");
    }

    private static object CreateObject(Type type, string path)
    {
        if (type.IsAbstract || type.IsInterface || type.IsPointer || type.IsByRefLike)
            throw new NotSupportedException($"Type '{type.FullName}' at '{path}' cannot be constructed.");
        try { return Activator.CreateInstance(type, nonPublic: true) ?? RuntimeHelpers.GetUninitializedObject(type); }
        catch (MissingMethodException) { return RuntimeHelpers.GetUninitializedObject(type); }
    }

    private static int[] ToIndices(int flatIndex, IReadOnlyList<int> dimensions)
    {
        var result = new int[dimensions.Count];
        for (var dimension = dimensions.Count - 1; dimension >= 0; dimension--)
        {
            result[dimension] = flatIndex % dimensions[dimension];
            flatIndex /= dimensions[dimension];
        }
        return result;
    }
}
