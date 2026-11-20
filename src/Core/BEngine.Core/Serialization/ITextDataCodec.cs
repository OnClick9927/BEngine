using System.Reflection;

namespace BEngine;

/// <summary>Text-format implementation supplied by a host. Binary data reading stays independent.</summary>
public interface ITextDataCodec
{
    string Serialize(object value);
    object Deserialize(string text, Type type, bool strict);
    byte[] Compile(ReadOnlySpan<byte> text, CancellationToken cancellationToken);
    void RegisterConverter(object converter);
    void UnregisterConverters(Assembly assembly);
    bool TryGetConverterVersion(object? exclusiveConverter, out int version);
}

/// <summary>Hosts install a text codec before loading editable documents.</summary>
public static class TextDataSerialization
{
    private static ITextDataCodec? _codec;
    public static ITextDataCodec? Codec => Volatile.Read(ref _codec);
    public static void RegisterCodec(ITextDataCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        Volatile.Write(ref _codec, codec);
    }
    internal static ITextDataCodec RequireCodec() => Codec ?? throw new NotSupportedException(
        "No text data codec is registered. Initialize the host's serialization support before reading text documents.");
}
