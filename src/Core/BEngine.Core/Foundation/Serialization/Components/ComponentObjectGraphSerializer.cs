using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Serialization;

internal static class ComponentObjectGraphSerializer
{
    private static readonly ConditionalWeakTable<MemberInfo, string[]> FormerMemberNames = new();

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
        BObject component,
        IEnumerable<MemberInfo>? members = null,
        bool allowTransientObjects = false)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component is MissingComponent missing && missing.serializedGraph is { } missingGraph)
            return Clone(missingGraph);

        var context = new WriteContext(allowTransientObjects, allowSceneReferences: component is Component);
        return new ComponentGraphData
        {
            Members = [.. (members ?? PersistentMembers(component))
                .Select(member => new SerializedMemberData
                {
                    Name = member.Name,
                    Value = context.Write(ComponentFieldSerializer.GetMemberValue(member, component),
                        ComponentFieldSerializer.GetMemberType(member), member.Name)
                })]
        };
    }

    internal static void Restore(
        BObject component,
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
        var members = BuildIndex2126(PersistentMembers(component), StringComparer.Ordinal);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo> BuildIndex2126(global::System.Collections.Generic.IEnumerable<global::System.Reflection.MemberInfo> indexSource2126, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2126)
        {
            ArgumentNullException.ThrowIfNull(indexSource2126, "source");
            var knownCount = indexSource2126.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo>(indexComparer2126);
            global::System.ReadOnlySpan<global::System.Reflection.MemberInfo> values;
            if (knownCount && indexSource2126 is global::System.Reflection.MemberInfo[] array)
                values = array;
            else if (knownCount && indexSource2126 is global::System.Collections.Generic.List<global::System.Reflection.MemberInfo> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo>(capacity, indexComparer2126);
                foreach (var item in indexSource2126)
                    result.Add(IndexKey2126(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo>(values.Length, indexComparer2126);
                foreach (var item in values)
                    result.Add(IndexKey2126(item), item);
                return result;
            }
        
            string IndexKey2126(global::System.Reflection.MemberInfo member) => member.Name;
        }
        foreach (var serializedMember in graph.Members)
        {
            var member = ResolveMember(serializedMember.Name, members.Values);
            if (member is null) continue;
            var memberType = ComponentFieldSerializer.GetMemberType(member);
            if (IsLegacyEmptyMask(serializedMember.Value, memberType)) continue;
            object? value;
            if (RuntimeObjectCodecs.Find(memberType) is { } codec && serializedMember.Value.Kind == "Scalar")
            {
                var referenceContext = BuildIndex2775(graph.Members.Where(item => item.Value.Kind == "Scalar"), StringComparer.Ordinal);
                [global::BEngine.SkipLoopGuard]
                global::System.Collections.Generic.Dictionary<string, string> BuildIndex2775(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.SerializedMemberData> indexSource2775, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2775)
                {
                    ArgumentNullException.ThrowIfNull(indexSource2775, "source");
                    var knownCount = indexSource2775.TryGetNonEnumeratedCount(out var capacity);
                    if (knownCount && capacity == 0)
                        return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer2775);
                    global::System.ReadOnlySpan<global::BEngine.Serialization.SerializedMemberData> values;
                    if (knownCount && indexSource2775 is global::BEngine.Serialization.SerializedMemberData[] array)
                        values = array;
                    else if (knownCount && indexSource2775 is global::System.Collections.Generic.List<global::BEngine.Serialization.SerializedMemberData> list)
                        values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
                    else
                    {
                        var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer2775);
                        foreach (var item in indexSource2775)
                            result.Add(IndexKey2775(item), IndexValue2775(item));
                        return result;
                    }
                
                    {
                        var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer2775);
                        foreach (var item in values)
                            result.Add(IndexKey2775(item), IndexValue2775(item));
                        return result;
                    }
                
                    string IndexKey2775(global::BEngine.Serialization.SerializedMemberData item) => item.Name;
                    string IndexValue2775(global::BEngine.Serialization.SerializedMemberData item) => item.Value.Value;
                }
                value = codec.ReadReference(serializedMember.Value.Value, referenceContext);
            }
            else value = context.Read(serializedMember.Value, memberType, serializedMember.Name);
            ComponentFieldSerializer.SetMemberValue(member, component, value);
        }
    }

    internal static ComponentGraphData Clone(ComponentGraphData graph) => ComponentGraphClone.Clone(graph);

    internal static IEnumerable<string> EnumerateAssetReferences(Type type, ComponentGraphData graph)
    {
        // Current graphs carry engine-object identity at every nesting level. Legacy
        // scalar graphs also need the declared root member type to identify assets.
        var members = ComponentFieldSerializer.GetSerializableMembers(type);
        var values = new Stack<SerializedValueData>();
        foreach (var item in graph.Members)
        {
            if (item.Value.Kind == "Scalar" && ResolveMember(item.Name, members) is { } member &&
                RuntimeObjectCodecs.IsReferenceType(ComponentFieldSerializer.GetMemberType(member)) &&
                !string.IsNullOrWhiteSpace(item.Value.Value))
                yield return item.Value.Value;
            values.Push(item.Value);
        }
        while (values.TryPop(out var value))
        {
            if (value.Kind == "EngineObject" && !string.IsNullOrWhiteSpace(value.Value) &&
                !value.Value.StartsWith("scene:", StringComparison.Ordinal) &&
                !value.Value.StartsWith("instance:", StringComparison.Ordinal) &&
                !value.Value.StartsWith("builtin-", StringComparison.Ordinal))
                yield return value.Value;
            foreach (var item in value.Items) values.Push(item);
            foreach (var item in value.Members) values.Push(item.Value);
            foreach (var item in value.Entries) { values.Push(item.Key); values.Push(item.Value); }
        }
    }

    internal static IEnumerable<MemberInfo> PersistentMembers(BObject value) => value is Component
        ? ComponentFieldSerializer.GetSerializableMembers(value.GetType())
        : GetNestedMembers(value.GetType()).Where(member => member.DeclaringType != typeof(BObject) && member.DeclaringType != typeof(BAsset));

    // Clone managed serialized data while preserving borrowed engine-object identity. Unlike
    // persistence, an in-memory clone must not reload referenced assets from their source files.
    internal static void CopySerializedState(BObject source, BObject destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var references = new Dictionary<string, BObject>(StringComparer.Ordinal);
        var writer = new WriteContext(allowTransientObjects: true, value =>
        {
            var key = $"instance:{value.GetInstanceID()}";
            references[key] = ReferenceEquals(value, source) ? destination : value;
            return key;
        });
        var reader = new ReadContext(null, reference => references.GetValueOrDefault(reference));
        var members = GetNestedMembers(source.GetType())
            .Where(member => member.DeclaringType != typeof(BObject) && member.DeclaringType != typeof(BAsset))
            .ToArray();
        var projectionSource6450 = members;
        ArgumentNullException.ThrowIfNull(projectionSource6450, "source");
        var projectionResult6450 = projectionSource6450.Length == 0 ? global::System.Array.Empty<global::BEngine.Serialization.SerializedValueData>() : new global::BEngine.Serialization.SerializedValueData[projectionSource6450.Length];
        for (var projectionIndex6450 = 0; projectionIndex6450 < projectionSource6450.Length; projectionIndex6450++)
        {
            var projectionItem6450 = projectionSource6450[projectionIndex6450];
            projectionResult6450[projectionIndex6450] = writer.Write(ComponentFieldSerializer.GetMemberValue(projectionItem6450, source), ComponentFieldSerializer.GetMemberType(projectionItem6450), projectionItem6450.Name);
        }
        
        var values = projectionResult6450;
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            ComponentFieldSerializer.SetMemberValue(member, destination,
                reader.Read(values[index], ComponentFieldSerializer.GetMemberType(member), member.Name));
        }
    }

    private static MemberInfo? ResolveMember(string name, IEnumerable<MemberInfo> members)
    {
        foreach (var member in members)
        {
            if (member.Name.Equals(name, StringComparison.Ordinal)) return member;
            foreach (var formerName in FormerMemberNames.GetValue(member, static key =>
                         key.GetCustomAttributes<FormerlySerializedAsAttribute>(inherit: true)
                             .Select(attribute => attribute.oldName).ToArray()))
                if (formerName.Equals(name, StringComparison.Ordinal)) return member;
        }
        return null;
    }

    private static IReadOnlyList<MemberInfo> GetNestedMembers(Type type)
    {
        var candidates = RuntimeTypeCache.GetInstanceMembers(type);
        Dictionary<string, MemberInfo>? unique = null;
        foreach (var member in candidates)
        {
            // Metadata providers can change without registering again. Evaluate every
            // candidate, including hidden base members, before choosing the first name.
            if (!RuntimeTypeCache.IsSerializableMember(member)) continue;
            // Private/ignored members must not inflate the temporary dictionary.
            unique ??= new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
            unique.TryAdd(member.Name, member);
        }
        if (unique is null) return Array.Empty<MemberInfo>();
        var members = new MemberInfo[unique.Count];
        unique.Values.CopyTo(members, 0);
        Array.Sort(members, static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return members;
    }

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
        type == typeof(Rect) || type == typeof(Hash128) || type == typeof(LayerMask);

    // Older graphs treated the immutable mask as an object with no writable members,
    // so no value was saved. Preserve the authored field fallback or constructor default.
    private static bool IsLegacyEmptyMask(SerializedValueData value, Type type) =>
        type == typeof(LayerMask) && value.Kind == "Object" && value.Members.Count == 0;

    private sealed class WriteContext(bool allowTransientObjects, Func<BObject, string>? objectReferenceWriter = null, bool allowSceneReferences = true)
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
            if (value is Delegate && allowTransientObjects)
                return New(ValueKind.NullValue, declaredType);
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
            if (!allowSceneReferences && value is (GameObject or Component))
                throw new InvalidDataException($"Asset member '{path}' cannot persist a scene object reference. Use an asset such as PrefabAsset instead.");
            var result = New(ValueKind.EngineObject, value.GetType());
            if (objectReferenceWriter is not null)
            {
                result.Value = objectReferenceWriter(value);
                return result;
            }
            result.Value = value switch
            {
                BAsset asset when asset.parentAssetGuid.HasValue && asset.localIdentifier > 0 =>
                    $"guid:{asset.parentAssetGuid.Value:N}#subasset={asset.localIdentifier.ToString(CultureInfo.InvariantCulture)}",
                _ when RuntimeObjectCodecs.Find(value.GetType()) is { } codec => codec.WriteReference(value),
                BAsset asset => asset.assetPath,
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
            var dimensions = new List<int>(array.Rank);
            for (var dimension = 0; dimension < array.Rank; dimension++)
                dimensions.Add(array.GetLength(dimension));
            result.Dimensions = dimensions;
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

    private sealed class ReadContext(
        Func<Guid, BObject?>? sceneObjectResolver, Func<string, BObject?>? objectReferenceResolver = null)
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
            // Migrate legacy path fields in v3 object graphs as well as v2 scalar scene fields.
            if (kind == ValueKind.Scalar && typeof(BAsset).IsAssignableFrom(declaredType))
                return string.IsNullOrWhiteSpace(data.Value) ? null : BAsset.Load(data.Value, declaredType);
            if (kind == ValueKind.Scalar && RuntimeObjectCodecs.Find(declaredType) is { } codec)
                return codec.ReadReference(data.Value);

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
            if (objectReferenceResolver is not null)
                result = objectReferenceResolver(reference);
            else if (reference.StartsWith("scene:", StringComparison.Ordinal))
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
            else result = RuntimeObjectCodecs.ReadReference(type, reference);

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
            if (Query28162(dimensions) || dimensions.Aggregate(1L, (count, length) => count * length) != data.Items.Count)
                throw new InvalidDataException($"Serialized array dimensions at '{path}' do not match its items.");
            [global::BEngine.SkipLoopGuard]
            bool Query28162(global::System.Collections.Generic.IEnumerable<int> querySource28162)
            {
                ArgumentNullException.ThrowIfNull(querySource28162, "source");
                bool Matches28162(int length) => length < 0;
                global::System.ReadOnlySpan<int> queryValues28162;
                if (querySource28162.GetType() == typeof(int[]))
                    queryValues28162 = (int[])querySource28162;
                else if (querySource28162.GetType() == typeof(global::System.Collections.Generic.List<int>))
                    queryValues28162 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<int>)querySource28162);
                else
                {
                    foreach (var length in querySource28162)
                        if (Matches28162(length))
                            return true;
                    return false;
                }
            
                foreach (var length in queryValues28162)
                    if (Matches28162(length))
                        return true;
                return false;
            }
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
            var members = BuildIndex26096(GetNestedMembers(type), StringComparer.Ordinal);
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo> BuildIndex26096(global::System.Collections.Generic.IEnumerable<global::System.Reflection.MemberInfo> indexSource26096, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer26096)
            {
                ArgumentNullException.ThrowIfNull(indexSource26096, "source");
                var knownCount = indexSource26096.TryGetNonEnumeratedCount(out var capacity);
                if (knownCount && capacity == 0)
                    return new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo>(indexComparer26096);
                global::System.ReadOnlySpan<global::System.Reflection.MemberInfo> values;
                if (knownCount && indexSource26096 is global::System.Reflection.MemberInfo[] array)
                    values = array;
                else if (knownCount && indexSource26096 is global::System.Collections.Generic.List<global::System.Reflection.MemberInfo> list)
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
                else
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo>(capacity, indexComparer26096);
                    foreach (var item in indexSource26096)
                        result.Add(IndexKey26096(item), item);
                    return result;
                }
            
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.MemberInfo>(values.Length, indexComparer26096);
                    foreach (var item in values)
                        result.Add(IndexKey26096(item), item);
                    return result;
                }
            
                string IndexKey26096(global::System.Reflection.MemberInfo member) => member.Name;
            }
            foreach (var serializedMember in data.Members)
            {
                var member = ResolveMember(serializedMember.Name, members.Values);
                if (member is null) continue;
                var memberType = ComponentFieldSerializer.GetMemberType(member);
                if (IsLegacyEmptyMask(serializedMember.Value, memberType)) continue;
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
        if (type == typeof(LayerMask)) return ((LayerMask)value).value.ToString(CultureInfo.InvariantCulture);
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
        if (type == typeof(LayerMask)) return new LayerMask(ulong.Parse(value, CultureInfo.InvariantCulture));
        if (type == typeof(Vector2))
        {
            Span<Fix64> parts = stackalloc Fix64[2];
            FixedScalarText.Parse(value, parts);
            return new Vector2(parts[0], parts[1]);
        }
        if (type == typeof(Vector4))
        {
            Span<Fix64> parts = stackalloc Fix64[4];
            FixedScalarText.Parse(value, parts);
            return new Vector4(parts[0], parts[1], parts[2],
                parts[3]);
        }
        if (type == typeof(Color))
        {
            Span<Fix64> parts = stackalloc Fix64[4];
            FixedScalarText.Parse(value, parts);
            return new Color(parts[0], parts[1], parts[2],
                parts[3]);
        }
        if (type == typeof(Rect))
        {
            Span<Fix64> parts = stackalloc Fix64[4];
            FixedScalarText.Parse(value, parts);
            return new Rect(parts[0], parts[1], parts[2],
                parts[3]);
        }
        if (type == typeof(Hash128)) return Hash128.Parse(value);
        throw new NotSupportedException($"Scalar type '{type.FullName}' is not supported.");
    }

    private static string Join(Fix64 a, Fix64 b) => $"{a.ToString()},{b.ToString()}";
    private static string Join(Fix64 a, Fix64 b, Fix64 c, Fix64 d) =>
        $"{a.ToString()},{b.ToString()},{c.ToString()},{d.ToString()}";


    private static Type GetListElementType(Type type) => type.IsArray
        ? type.GetElementType()!
        : FindCollectionInterface(type, typeof(IList<>))?.GetGenericArguments()[0] ?? typeof(object);

    private static (Type Key, Type Value) GetDictionaryTypes(Type type)
    {
        var dictionaryType = FindCollectionInterface(type, typeof(IDictionary<,>));
        if (dictionaryType is null) return (typeof(object), typeof(object));
        var arguments = dictionaryType.GetGenericArguments();
        return (arguments[0], arguments[1]);
    }

    private static Type? FindCollectionInterface(Type type, Type genericInterface)
    {
        // Preserve interface order, including types implementing multiple collection interfaces.
        foreach (var candidate in type.GetInterfaces())
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericInterface)
                return candidate;
        return type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface ? type : null;
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
