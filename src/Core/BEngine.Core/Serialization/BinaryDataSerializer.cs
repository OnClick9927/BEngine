using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace BEngine;

/// <summary>
/// Compiles plain YAML data at build time and binds the compact result to an explicitly requested
/// data type at runtime. The document cannot name or instantiate arbitrary CLR types. YAML remains
/// the editable source; opt-in compilation rejects aliases, custom tags and non-scalar map keys.
/// </summary>
public static class BinaryDataSerializer
{
    public const int MaximumDocumentBytes = 64 * 1024 * 1024;
    private const int MaximumStringBytes = 1024 * 1024;
    private const int MaximumStrings = 262144;
    private const int MaximumNodes = 2_000_000;
    private const int MaximumCollectionCount = 1_000_000;
    private const int MaximumDepth = 64;
    private static readonly byte[] Magic = [(byte)'B', (byte)'D', (byte)'A', (byte)'T', 1, 0, 0, 0];
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly ConditionalWeakTable<Type, TypePlan> Plans = new();
    private static readonly ConditionalWeakTable<Type, BindingPlan> BindingPlans = new();
    private enum Kind : byte { Null, Scalar, Sequence, Mapping }

    public static bool IsBinaryData(ReadOnlySpan<byte> data) => data.Length >= Magic.Length &&
        data[..4].SequenceEqual(Magic.AsSpan(0, 4)) && data[5] == 0 && data[6] == 0 && data[7] == 0;

    /// <summary>Compiles one plain data document. Unsupported YAML constructs fail the build explicitly.</summary>
    public static byte[] CompileYaml(ReadOnlySpan<byte> yaml, CancellationToken cancellationToken = default) =>
        TextDataSerialization.RequireCodec().Compile(yaml, cancellationToken);

    public static T Deserialize<T>(ReadOnlySpan<byte> bytes, bool strict = false) => (T)Deserialize(bytes, typeof(T), strict);

    public static object Deserialize(ReadOnlySpan<byte> bytes, Type type, bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (bytes.Length > MaximumDocumentBytes || bytes.Length < Magic.Length + 8 || !bytes[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Unsupported or oversized compiled data document.");
        try
        {
            var reader = new Reader(bytes, strict);
            var value = reader.Read(type, 0) ?? throw new InvalidDataException("The compiled data root cannot be null.");
            reader.Finish();
            SerializationCallbackBridge.AfterDeserialize(value);
            return value;
        }
        catch (Exception exception) when (exception is DecoderFallbackException or FormatException or OverflowException or TargetInvocationException or ArgumentException)
        { throw new InvalidDataException($"Compiled data cannot be bound to '{type.FullName}'.", exception); }
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly string[] _strings;
        private readonly bool _strict;
        private int _position, _nodes;

        internal Reader(ReadOnlySpan<byte> data, bool strict)
        {
            _data = data; _position = Magic.Length; _nodes = 0; _strict = strict; _strings = [];
            int count = Count(MaximumStrings);
            if ((long)count * 4 > _data.Length - _position) throw new InvalidDataException("Compiled string count exceeds remaining input.");
            var values = new string[count];
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            for (int i = 0; i < count; i++)
            {
                int length = Count(MaximumStringBytes); total = checked(total + length);
                if (total > MaximumDocumentBytes) throw new InvalidDataException("Compiled string table exceeds 64 MiB.");
                values[i] = Utf8.GetString(Take(length));
                if (!unique.Add(values[i])) throw new InvalidDataException("Compiled string table contains duplicate entries.");
            }
            _strings = values;
            int bodyLength = Count(MaximumDocumentBytes);
            if (bodyLength != _data.Length - _position) throw new InvalidDataException("Compiled data body length is invalid or contains trailing bytes.");
        }

        internal object? Read(Type expected, int depth)
        {
            if (depth > MaximumDepth || ++_nodes > MaximumNodes) throw new InvalidDataException("Compiled data depth or node limit exceeded.");
            return ReadValue((Kind)Take(1)[0], expected, depth);
        }

        private object? ReadValue(Kind kind, Type expected, int depth)
        {
            // String tables dominate plain-data documents. These reference types
            // need neither nullable reflection nor scalar conversion.
            if (expected == typeof(string) || expected == typeof(object))
            {
                if (kind == Kind.Null) return null;
                if (kind == Kind.Scalar) return Text();
            }
            var binding = BindingPlans.GetValue(expected, static type => new BindingPlan(type));
            if (kind == Kind.Null)
            {
                if (!binding.AllowNull) throw new InvalidDataException($"Null is invalid for '{expected.Name}'.");
                return null;
            }
            if (binding.Adapter is { } adapter)
            {
                // An adapter changes the requested model, not the wire tree. Reuse
                // this node and its depth; ordinary bindings need no extra lookup.
                return adapter.RestoreData(ReadValue(kind, adapter.DataType, depth)!);
            }
            expected = binding.Type;
            if (kind == Kind.Scalar) return ConvertScalar(Text(), expected);
            if (kind == Kind.Sequence)
            {
                int count = Count(MaximumCollectionCount);
                if (count > _data.Length - _position) throw new InvalidDataException("Compiled sequence count exceeds remaining input.");
                var element = binding.SequenceElement ?? throw new InvalidDataException($"Compiled sequence cannot bind to '{expected.Name}'.");
                if (binding.IsArray)
                {
                    var array = Array.CreateInstance(element, count);
                    for (int i = 0; i < count; i++) array.SetValue(Read(element, depth + 1), i);
                    return array;
                }
                var list = expected == typeof(object) ? new List<object?>(count) : (IList)Activator.CreateInstance(expected, count)!;
                for (int i = 0; i < count; i++) list.Add(Read(element, depth + 1));
                return list;
            }
            if (kind != Kind.Mapping) throw new InvalidDataException("Compiled data node kind is invalid.");
            int members = Count(MaximumCollectionCount);
            if ((long)members * 5 > _data.Length - _position) throw new InvalidDataException("Compiled map count exceeds remaining input.");
            Span<int> smallKeys = members <= 16 ? stackalloc int[members] : default;
            var seen = new SeenKeys(smallKeys, members);
            if (expected == typeof(Fix64))
            {
                if (members != 1 || Key(ref seen, Count(_strings.Length - 1), _strings) is not ("rawValue" or "RawValue"))
                    throw new InvalidDataException("A legacy Fix64 mapping requires exactly one rawValue.");
                return Fix64.FromRaw((long)Read(typeof(long), depth + 1)!);
            }
            if (binding.DictionaryValue is { } valueType)
            {
                var dictionary = expected == typeof(object) ? new Dictionary<string, object?>(StringComparer.Ordinal) : (IDictionary)Activator.CreateInstance(expected)!;
                for (int i = 0; i < members; i++)
                {
                    var key = Key(ref seen, Count(_strings.Length - 1), _strings);
                    object typedKey = binding.DictionaryKey == typeof(Guid) ? Guid.Parse(key) : key;
                    // Different textual GUID spellings can denote the same key. Match the
                    // YAML dictionary's duplicate rejection after scalar conversion as well.
                    if (dictionary.Contains(typedKey)) throw new InvalidDataException("Compiled data map contains duplicate converted keys.");
                    dictionary.Add(typedKey, Read(valueType, depth + 1));
                }
                return dictionary;
            }
            if (!binding.PlainMapping)
                throw new InvalidDataException($"'{expected.Name}' is not a plain compiled data type.");
            var plan = Plans.GetValue(expected, static type => new TypePlan(type));
            var result = Activator.CreateInstance(expected) ?? throw new InvalidDataException($"'{expected.Name}' needs a public default constructor.");
            for (int i = 0; i < members; i++)
            {
                var name = Key(ref seen, Count(_strings.Length - 1), _strings);
                if (plan.Members.TryGetValue(name, out var member))
                {
                    var value = Read(member.Type, depth + 1);
                    if (_strict && value is null && !member.AllowNull) throw new InvalidDataException($"Null is invalid for '{expected.Name}.{name}'.");
                    member.Set(result, value);
                }
                else if (plan.UnsupportedMembers.Contains(name)) throw new InvalidDataException($"Compiled data requires a public writable member: '{expected.Name}.{name}'.");
                else if (_strict) throw new InvalidDataException($"Unknown compiled data member '{expected.Name}.{name}'.");
                else Skip(depth + 1);
            }
            return result;
        }

        private static string Key(scoped ref SeenKeys seen, int index, string[] strings)
        {
            if (!seen.Add(index)) throw new InvalidDataException("Compiled data map contains duplicate keys.");
            return strings[index];
        }
        private void Skip(int depth)
        {
            if (depth > MaximumDepth || ++_nodes > MaximumNodes) throw new InvalidDataException("Compiled data depth or node limit exceeded.");
            var kind = (Kind)Take(1)[0];
            if (kind == Kind.Null) return;
            if (kind == Kind.Scalar) { _ = Text(); return; }
            if (kind is not (Kind.Sequence or Kind.Mapping)) throw new InvalidDataException("Invalid skipped compiled node.");
            int count = Count(MaximumCollectionCount);
            if ((long)count * (kind == Kind.Mapping ? 5 : 1) > _data.Length - _position) throw new InvalidDataException("Invalid skipped collection length.");
            var keyCount = kind == Kind.Mapping ? count : 0;
            Span<int> smallKeys = keyCount <= 16 ? stackalloc int[keyCount] : default;
            var keys = new SeenKeys(smallKeys, keyCount);
            for (int i = 0; i < count; i++) { if (kind == Kind.Mapping) _ = Key(ref keys, Count(_strings.Length - 1), _strings); Skip(depth + 1); }
        }
        internal void Finish() { if (_position != _data.Length) throw new InvalidDataException("Compiled data contains trailing bytes."); }
        private string Text() => _strings[Count(_strings.Length - 1)];
        private int Count(int maximum)
        {
            int value = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(Take(4));
            if (value < 0 || value > maximum) throw new InvalidDataException("Compiled data length, count or string reference is invalid.");
            return value;
        }
        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _data.Length - _position) throw new InvalidDataException("Compiled data is truncated.");
            var bytes = _data.Slice(_position, count); _position += count; return bytes;
        }
    }

    // The key table already guarantees distinct strings, so comparing interned
    // indices preserves duplicate-key validation without allocating for small maps.
    private ref struct SeenKeys(Span<int> small, int count)
    {
        private readonly Span<int> _small = small;
        private readonly HashSet<int>? _large = count > 16 ? new() : null;
        private int _count;
        internal bool Add(int key)
        {
            if (_large is { } large) return large.Add(key);
            if (_small[.._count].Contains(key)) return false;
            _small[_count++] = key;
            return true;
        }
    }

    // Ephemeron keys allow collectible project types and their member types to
    // unload; a static dictionary would keep hot-update assemblies alive.
    private sealed class BindingPlan
    {
        internal readonly Type Type;
        internal readonly Serialization.SerializationDataAdapter? Adapter;
        internal readonly Type? SequenceElement, DictionaryKey, DictionaryValue;
        internal readonly bool AllowNull, IsArray, PlainMapping;
        internal BindingPlan(Type type)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            AllowNull = !type.IsValueType || nullable is not null;
            Type = nullable ?? type;
            Adapter = Serialization.SerializationDataAdapter.Find(Type);
            IsArray = Type.IsArray;
            if (IsArray && Type.GetArrayRank() == 1) SequenceElement = Type.GetElementType();
            else if (Type == typeof(object)) SequenceElement = DictionaryValue = typeof(object);
            else if (Type.IsGenericType)
            {
                var definition = Type.GetGenericTypeDefinition();
                if (definition == typeof(List<>)) SequenceElement = Type.GetGenericArguments()[0];
                else if (definition == typeof(Dictionary<,>))
                {
                    var arguments = Type.GetGenericArguments();
                    if (arguments[0] == typeof(string) || arguments[0] == typeof(Guid))
                    { DictionaryKey = arguments[0]; DictionaryValue = arguments[1]; }
                }
            }
            PlainMapping = !(Type.IsPrimitive || Type.IsEnum || Type == typeof(decimal) || Type == typeof(Guid) ||
                Type == typeof(DateTime) || Type == typeof(DateTimeOffset) || Type == typeof(TimeSpan) ||
                typeof(IEnumerable).IsAssignableFrom(Type) || SerializationCallbackBridge.IsEngineObject(Type) ||
                Type.IsAbstract || Type.IsInterface || Type.IsPointer || typeof(Delegate).IsAssignableFrom(Type));
        }
    }

    private static object ConvertScalar(string text, Type type)
    {
        if (type == typeof(string) || type == typeof(object)) return text;
        if (type == typeof(Fix64)) return Fix64.Parse(text, CultureInfo.InvariantCulture);
        if (type.IsEnum)
        {
            try { return Enum.Parse(type, text, ignoreCase: true); }
            catch (ArgumentException) { return SerializationCallbackBridge.DeserializeScalar(text, type); }
        }
        if (type == typeof(Guid)) return Guid.Parse(text);
        if (type == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return text.ToLowerInvariant() switch { "true" or "yes" or "y" or "on" => true, "false" or "no" or "n" or "off" => false, _ => throw new FormatException("Invalid compiled boolean.") };
        if (type.IsPrimitive || type == typeof(decimal))
        {
            try { return Convert.ChangeType(text, type, CultureInfo.InvariantCulture); }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                // Retain the source format's less common scalar conventions (hexadecimal,
                // digit separators, infinity) without paying for YAML token binding per field.
                return SerializationCallbackBridge.DeserializeScalar(text, type);
            }
        }
        throw new InvalidDataException($"Compiled scalar cannot bind to '{type.Name}'.");
    }

    private sealed class MemberPlan(Type type, Action<object, object?> set, MemberInfo member, TypePlan owner)
    {
        private int _allowNull;
        internal Type Type { get; } = type;
        internal bool AllowNull
        {
            get
            {
                var cached = Volatile.Read(ref _allowNull);
                return cached == 0 ? owner.GetAllowNull(member, ref _allowNull) : cached == 2;
            }
        }
        internal void Set(object target, object? value) => set(target, value);
    }
    private sealed class TypePlan
    {
        internal readonly Dictionary<string, MemberPlan> Members = new(StringComparer.Ordinal);
        internal readonly HashSet<string> UnsupportedMembers = new(StringComparer.Ordinal);
        private readonly object _nullabilityGate = new();
        private NullabilityInfoContext? _nullability;
        internal TypePlan(Type type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || SerializationMetadata.GetMember(property).Ignored) continue;
                if (property.SetMethod is { IsPublic: true })
                    Add(property, new(property.PropertyType, CreatePropertySetter(property), property, this));
                else UnsupportedMembers.Add(Name(property));
            }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (SerializationMetadata.GetMember(field).Ignored) continue;
                if (!field.IsInitOnly && !field.IsLiteral)
                    Add(field, new(field.FieldType, field.SetValue, field, this));
                else UnsupportedMembers.Add(Name(field));
            }
        }
        internal bool GetAllowNull(MemberInfo member, ref int cached)
        {
            lock (_nullabilityGate)
            {
                var state = Volatile.Read(ref cached);
                if (state != 0) return state == 2;
                var context = _nullability ??= new NullabilityInfoContext();
                var allowNull = (member switch
                {
                    PropertyInfo property => context.Create(property).WriteState,
                    FieldInfo field => context.Create(field).WriteState,
                    _ => throw new InvalidOperationException("Unsupported compiled data member.")
                }) != NullabilityState.NotNull;
                Volatile.Write(ref cached, allowNull ? 2 : 1);
                return allowNull;
            }
        }
        private void Add(MemberInfo member, MemberPlan plan)
        {
            var name = Name(member);
            if (!Members.TryAdd(name, plan)) throw new InvalidDataException($"Ambiguous compiled data member '{member.DeclaringType?.Name}.{name}'.");
        }
        private static string Name(MemberInfo member) => SerializationMetadata.GetMember(member).Name;
    }

    private static Action<object, object?> CreatePropertySetter(PropertyInfo property)
    {
        // Preserve custom PropertyInfo overrides and boxed value-type mutation.
        // The normal runtime class-property path can avoid per-value reflection.
        if (property.GetType().Assembly != typeof(PropertyInfo).Assembly ||
            property.DeclaringType is not { IsValueType: false } owner || owner.ContainsGenericParameters)
            return property.SetValue;
        var factory = PropertySetterFactory.Method;
        if (factory is null) return property.SetValue;
        try
        {
            return (Action<object, object?>)factory.MakeGenericMethod(owner, property.PropertyType)
                .Invoke(null, [property.SetMethod!])!;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or
            TargetInvocationException or MemberAccessException)
        { return property.SetValue; }
    }

    private static Action<object, object?> CreateTypedPropertySetter<TTarget, TValue>(MethodInfo method)
        where TTarget : class
    {
        var setter = method.CreateDelegate<Action<TTarget, TValue>>();
        return (target, value) =>
        {
            try { setter((TTarget)target, (TValue)value!); }
            // Reflection wraps user setter failures, even if the setter itself
            // throws TargetInvocationException. Keep that public error chain.
            catch (Exception error) { throw new TargetInvocationException(error); }
        };
    }

    private static class PropertySetterFactory
    {
        internal static readonly MethodInfo? Method = typeof(BinaryDataSerializer).GetMethod(
            nameof(CreateTypedPropertySetter), BindingFlags.Static | BindingFlags.NonPublic);
    }
}
