using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

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
    private enum Kind : byte { Null, Scalar, Sequence, Mapping }

    public static bool IsBinaryData(ReadOnlySpan<byte> data) => data.Length >= Magic.Length &&
        data[..4].SequenceEqual(Magic.AsSpan(0, 4)) && data[5] == 0 && data[6] == 0 && data[7] == 0;

    /// <summary>Compiles one plain data document. Unsupported YAML constructs fail the build explicitly.</summary>
    public static byte[] CompileYaml(ReadOnlySpan<byte> yaml, CancellationToken cancellationToken = default)
    {
        if (yaml.Length == 0 || yaml.Length > MaximumDocumentBytes)
            throw new InvalidDataException("Compiled YAML input must contain 1 byte to 64 MiB.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var compiler = new Compiler(Utf8.GetString(yaml), cancellationToken);
            return compiler.Compile();
        }
        catch (Exception exception) when (exception is YamlException or DecoderFallbackException)
        { throw new InvalidDataException("The compiled data source is not valid plain UTF-8 YAML.", exception); }
    }

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
            SerializationCallbackUtility.AfterDeserialize(value);
            return value;
        }
        catch (Exception exception) when (exception is DecoderFallbackException or FormatException or OverflowException or TargetInvocationException or ArgumentException)
        { throw new InvalidDataException($"Compiled data cannot be bound to '{type.FullName}'.", exception); }
    }

    private sealed class Compiler
    {
        private readonly IParser _parser;
        private readonly CancellationToken _cancellation;
        private readonly List<string> _strings = [];
        private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
        private readonly MemoryStream _body = new();
        private readonly BinaryWriter _writer;
        private int _nodes, _stringBytes;

        internal Compiler(string yaml, CancellationToken cancellation)
        {
            _parser = new Parser(new StringReader(yaml)); _cancellation = cancellation;
            _writer = new BinaryWriter(_body, Utf8, leaveOpen: true);
        }

        internal byte[] Compile()
        {
            using (_body) using (_writer)
            {
                _parser.Consume<StreamStart>(); _parser.Consume<DocumentStart>();
                WriteNode(0);
                _parser.Consume<DocumentEnd>(); _parser.Consume<StreamEnd>();
                _cancellation.ThrowIfCancellationRequested();
                if (Magic.Length + 8L + _strings.Count * 4L + _stringBytes + _body.Length > MaximumDocumentBytes)
                    throw new InvalidDataException("Compiled data exceeds 64 MiB.");
                using var output = new MemoryStream();
                using var writer = new BinaryWriter(output, Utf8, leaveOpen: true);
                writer.Write(Magic); writer.Write(_strings.Count);
                foreach (var text in _strings)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    var bytes = Utf8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes);
                }
                writer.Write(checked((int)_body.Length)); _body.Position = 0; _body.CopyTo(output);
                if (output.Length > MaximumDocumentBytes) throw new InvalidDataException("Compiled data exceeds 64 MiB.");
                return output.ToArray();
            }
        }

        private int Intern(string value)
        {
            if (_indices.TryGetValue(value, out var index)) return index;
            var bytes = Utf8.GetByteCount(value);
            if (bytes > MaximumStringBytes || _strings.Count >= MaximumStrings || (_stringBytes += bytes) > MaximumDocumentBytes)
                throw new InvalidDataException("Compiled data string limits were exceeded.");
            index = _strings.Count; _strings.Add(value); _indices.Add(value, index); return index;
        }

        private void WriteNode(int depth)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (depth > MaximumDepth || ++_nodes > MaximumNodes || _body.Length > MaximumDocumentBytes)
                throw new InvalidDataException("Compiled data depth, node or size limits were exceeded.");
            if (_parser.TryConsume<Scalar>(out var scalar))
            {
                ValidateNode(scalar);
                // The default deserializer treats unquoted empty/null/~ as null, including string properties.
                bool isNull = scalar.Style is ScalarStyle.Plain && scalar.Value is "" or "~" or "null" or "Null" or "NULL";
                _writer.Write((byte)(isNull ? Kind.Null : Kind.Scalar));
                if (!isNull) _writer.Write(Intern(scalar.Value));
                return;
            }
            if (_parser.TryConsume<SequenceStart>(out var sequence))
            {
                ValidateNode(sequence);
                _writer.Write((byte)Kind.Sequence); var position = _body.Position; _writer.Write(0); int count = 0;
                while (!_parser.TryConsume<SequenceEnd>(out _))
                {
                    if (++count > MaximumCollectionCount) throw new InvalidDataException("Compiled data sequence is too large.");
                    WriteNode(depth + 1);
                }
                PatchCount(position, count); return;
            }
            if (_parser.TryConsume<MappingStart>(out var mapping))
            {
                ValidateNode(mapping);
                _writer.Write((byte)Kind.Mapping); var position = _body.Position; _writer.Write(0); int count = 0;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                while (!_parser.TryConsume<MappingEnd>(out _))
                {
                    if (++count > MaximumCollectionCount) throw new InvalidDataException("Compiled data map is too large.");
                    var key = _parser.Consume<Scalar>(); ValidateNode(key);
                    if (!keys.Add(key.Value) || key.Value == "<<") throw new InvalidDataException("Compiled data maps require unique literal scalar keys; YAML merges are unsupported.");
                    _writer.Write(Intern(key.Value)); WriteNode(depth + 1);
                }
                PatchCount(position, count); return;
            }
            throw new InvalidDataException("Compiled data supports scalar, sequence and mapping nodes; aliases are unsupported.");
        }

        private static void ValidateNode(NodeEvent node)
        {
            if (!node.Anchor.IsEmpty) throw new InvalidDataException("Compiled YAML data cannot contain anchors or aliases.");
            if (!node.Tag.IsEmpty)
                throw new InvalidDataException("Compiled YAML data cannot contain explicit or custom tags; keep this document as YAML instead.");
        }
        private void PatchCount(long position, int count)
        { var end = _body.Position; _body.Position = position; _writer.Write(count); _body.Position = end; }
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
            var kind = (Kind)Take(1)[0];
            var nullable = Nullable.GetUnderlyingType(expected);
            if (kind == Kind.Null)
            {
                if (expected.IsValueType && nullable is null) throw new InvalidDataException($"Null is invalid for '{expected.Name}'.");
                return null;
            }
            expected = nullable ?? expected;
            if (kind == Kind.Scalar) return ConvertScalar(Text(), expected);
            if (kind == Kind.Sequence)
            {
                int count = Count(MaximumCollectionCount);
                if (count > _data.Length - _position) throw new InvalidDataException("Compiled sequence count exceeds remaining input.");
                Type element;
                if (expected.IsArray && expected.GetArrayRank() == 1) element = expected.GetElementType()!;
                else if (expected.IsGenericType && expected.GetGenericTypeDefinition() == typeof(List<>)) element = expected.GetGenericArguments()[0];
                else if (expected == typeof(object)) element = typeof(object);
                else throw new InvalidDataException($"Compiled sequence cannot bind to '{expected.Name}'.");
                if (expected.IsArray)
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
            var seen = new HashSet<int>();
            if (expected == typeof(Fix64))
            {
                if (members != 1 || Key(seen) is not ("rawValue" or "RawValue"))
                    throw new InvalidDataException("A legacy Fix64 mapping requires exactly one rawValue.");
                return Fix64.FromRaw((long)Read(typeof(long), depth + 1)!);
            }
            var isDictionary = expected.IsGenericType && expected.GetGenericTypeDefinition() == typeof(Dictionary<,>) && expected.GetGenericArguments()[0] == typeof(string);
            if (isDictionary || expected == typeof(object))
            {
                var valueType = isDictionary ? expected.GetGenericArguments()[1] : typeof(object);
                var dictionary = expected == typeof(object) ? new Dictionary<string, object?>(StringComparer.Ordinal) : (IDictionary)Activator.CreateInstance(expected)!;
                for (int i = 0; i < members; i++) { var key = Key(seen); dictionary.Add(key, Read(valueType, depth + 1)); }
                return dictionary;
            }
            if (expected.IsPrimitive || expected.IsEnum || expected == typeof(decimal) || expected == typeof(Guid) ||
                expected == typeof(DateTime) || expected == typeof(DateTimeOffset) || expected == typeof(TimeSpan) ||
                typeof(IEnumerable).IsAssignableFrom(expected) ||
                typeof(BObject).IsAssignableFrom(expected) || expected.IsAbstract || expected.IsInterface || expected.IsPointer || typeof(Delegate).IsAssignableFrom(expected))
                throw new InvalidDataException($"'{expected.Name}' is not a plain compiled data type.");
            var plan = Plans.GetValue(expected, static type => new TypePlan(type));
            var result = Activator.CreateInstance(expected) ?? throw new InvalidDataException($"'{expected.Name}' needs a public default constructor.");
            for (int i = 0; i < members; i++)
            {
                var name = Key(seen);
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

        private string Key(HashSet<int> seen)
        {
            int index = Count(_strings.Length - 1);
            if (!seen.Add(index)) throw new InvalidDataException("Compiled data map contains duplicate keys.");
            return _strings[index];
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
            HashSet<int>? keys = kind == Kind.Mapping ? [] : null;
            for (int i = 0; i < count; i++) { if (keys is not null) _ = Key(keys); Skip(depth + 1); }
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

    private static object ConvertScalar(string text, Type type)
    {
        if (type == typeof(string) || type == typeof(object)) return text;
        if (type == typeof(Fix64)) return Fix64.Parse(text, CultureInfo.InvariantCulture);
        if (type.IsEnum)
        {
            try { return Enum.Parse(type, text, ignoreCase: true); }
            catch (ArgumentException) { return YamlUtility.Deserialize(text, type); }
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
                return YamlUtility.Deserialize(text, type);
            }
        }
        throw new InvalidDataException($"Compiled scalar cannot bind to '{type.Name}'.");
    }

    private sealed class MemberPlan(Type type, Action<object, object?> set, bool allowNull)
    {
        internal Type Type { get; } = type;
        internal bool AllowNull { get; } = allowNull;
        internal void Set(object target, object? value) => set(target, value);
    }
    private sealed class TypePlan
    {
        internal readonly Dictionary<string, MemberPlan> Members = new(StringComparer.Ordinal);
        internal readonly HashSet<string> UnsupportedMembers = new(StringComparer.Ordinal);
        internal TypePlan(Type type)
        {
            var nullability = new NullabilityInfoContext();
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || property.IsDefined(typeof(YamlIgnoreAttribute))) continue;
                if (property.SetMethod is { IsPublic: true })
                    Add(property, new(property.PropertyType, property.SetValue, nullability.Create(property).WriteState != NullabilityState.NotNull));
                else UnsupportedMembers.Add(Name(property));
            }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.IsDefined(typeof(YamlIgnoreAttribute))) continue;
                if (!field.IsInitOnly && !field.IsLiteral)
                    Add(field, new(field.FieldType, field.SetValue, nullability.Create(field).WriteState != NullabilityState.NotNull));
                else UnsupportedMembers.Add(Name(field));
            }
        }
        private void Add(MemberInfo member, MemberPlan plan)
        {
            var name = Name(member);
            if (!Members.TryAdd(name, plan)) throw new InvalidDataException($"Ambiguous compiled data member '{member.DeclaringType?.Name}.{name}'.");
        }
        private static string Name(MemberInfo member)
        {
            var setting = member.GetCustomAttribute<YamlMemberAttribute>();
            if (!string.IsNullOrEmpty(setting?.Alias)) return setting.Alias;
            return setting is { ApplyNamingConventions: false } ? member.Name :
                YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance.Apply(member.Name);
        }
    }
}
