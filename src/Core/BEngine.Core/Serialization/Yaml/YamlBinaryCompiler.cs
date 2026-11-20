using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace BEngine;

internal static class YamlBinaryCompiler
{
    public const int MaximumDocumentBytes = 64 * 1024 * 1024;
    private const int MaximumStringBytes = 1024 * 1024;
    private const int MaximumStrings = 262144;
    private const int MaximumNodes = 2_000_000;
    private const int MaximumCollectionCount = 1_000_000;
    private const int MaximumDepth = 64;
    private static readonly byte[] Magic = [(byte)'B', (byte)'D', (byte)'A', (byte)'T', 1, 0, 0, 0];
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private enum Kind : byte { Null, Scalar, Sequence, Mapping }
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

}
