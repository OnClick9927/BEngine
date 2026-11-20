using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text;
using YamlDotNet.Core;

namespace BEngine;

internal sealed class YamlTextCodec : ITextDataCodec
{
    private static readonly object ConverterGate = new();
    private static CodecGeneration Codecs = new([], 0);

    internal static bool TryGetDefaultConverterVersion(out int version)
    {
        var codecs = Volatile.Read(ref Codecs);
        version = codecs.Version;
        return codecs.Converters.Length == 0;
    }

    // A package may use a compiled DTO only while its exact converter remains
    // the sole registered override. Never retain the caller or expose a mutable
    // converter array; late registrations/removals publish another generation.
    internal static bool TryGetExclusiveConverterVersion(IYamlTypeConverter expected, out int version)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var codecs = Volatile.Read(ref Codecs);
        version = codecs.Version;
        return codecs.Converters.Length == 1 && ReferenceEquals(codecs.Converters[0], expected);
    }

    private static ISerializer CreateSerializer(IYamlTypeConverter[] converters) => Configure(new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .WithQuotingNecessaryStrings()
        .WithMaximumRecursion(64)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull), converters)
        .Build();

    private static IDeserializer CreateDeserializer(bool strict, IYamlTypeConverter[] converters)
    {
        var builder = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .WithMaximumRecursion(64);
        return Configure(strict ? builder.WithEnforceNullability() : builder.IgnoreUnmatchedProperties(), converters).Build();
    }

    private static SerializerBuilder Configure(SerializerBuilder builder, IYamlTypeConverter[] converters)
    {
        builder.WithTypeInspector(inner => new CoreYamlTypeInspector(inner), where => where.InsteadOf<YamlAttributesTypeInspector>());
        builder.WithTypeConverter(Fix64YamlConverter.Instance);
        builder.WithTypeConverter(new SerializationDataYamlConverter());
        foreach (var converter in converters) builder.WithTypeConverter(converter);
        return builder;
    }
    private static DeserializerBuilder Configure(DeserializerBuilder builder, IYamlTypeConverter[] converters)
    {
        builder.WithTypeInspector(inner => new CoreYamlTypeInspector(inner), where => where.InsteadOf<YamlAttributesTypeInspector>());
        builder.WithTypeConverter(Fix64YamlConverter.Instance);
        builder.WithTypeConverter(new SerializationDataYamlConverter());
        foreach (var converter in converters) builder.WithTypeConverter(converter);
        return builder;
    }
    private static void RegisterTypedConverter(IYamlTypeConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        lock (ConverterGate)
        {
            var current = Codecs;
            PublishConverters(current.Converters.Where(c => c.GetType() != converter.GetType()).Append(converter).ToArray(),
                unchecked(current.Version + 1));
        }
    }
    public void UnregisterConverters(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (ConverterGate)
        {
            var current = Codecs;
            var remaining = current.Converters.Where(c => c.GetType().Assembly != assembly).ToArray();
            if (remaining.Length != current.Converters.Length)
                PublishConverters(remaining, unchecked(current.Version + 1));
            else
                // YAML codecs cache reflected DTO types even when their assembly contributes no
                // converter. Drop those caches on unload too; retain the converter version so
                // unchanged converter sets keep their compiled-data compatibility. Rebuild lazily.
                Volatile.Write(ref Codecs, new CodecGeneration(current.Converters, current.Version));
        }
    }

    private static void PublishConverters(IYamlTypeConverter[] converters, int version)
    {
        var next = new CodecGeneration(converters, version);
        // Registration still validates all codecs synchronously. Only a complete
        // generation is visible; failed rebuilds leave existing readers unchanged.
        _ = next.Serializer.Value;
        _ = next.Deserializer.Value;
        _ = next.StrictDeserializer.Value;
        Volatile.Write(ref Codecs, next);
    }

    private sealed class CodecGeneration
    {
        internal readonly IYamlTypeConverter[] Converters;
        internal readonly int Version;
        internal readonly Lazy<ISerializer> Serializer;
        internal readonly Lazy<IDeserializer> Deserializer;
        internal readonly Lazy<IDeserializer> StrictDeserializer;

        internal CodecGeneration(IYamlTypeConverter[] converters, int version)
        {
            Converters = converters;
            Version = version;
            Serializer = new(() => CreateSerializer(converters), LazyThreadSafetyMode.ExecutionAndPublication);
            Deserializer = new(() => CreateDeserializer(false, converters), LazyThreadSafetyMode.ExecutionAndPublication);
            StrictDeserializer = new(() => CreateDeserializer(true, converters), LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }
    public string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Volatile.Read(ref Codecs).Serializer.Value.Serialize(value).ReplaceLineEndings("\n");
    }


    public object Deserialize(string yaml, Type type, bool strict = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        ArgumentNullException.ThrowIfNull(type);
        try
        {
            var codecs = Volatile.Read(ref Codecs);
            var value = (strict ? codecs.StrictDeserializer : codecs.Deserializer).Value.Deserialize(new StringReader(yaml), type) ??
                        throw new InvalidDataException($"The YAML document for {type.Name} is empty.");
            return value;
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"The YAML document for {type.Name} is invalid: {exception.Message}", exception);
        }
    }

    public void RegisterConverter(object converter) => RegisterTypedConverter(converter as IYamlTypeConverter ??
        throw new ArgumentException("A YAML converter must implement IYamlTypeConverter.", nameof(converter)));
    public bool TryGetConverterVersion(object? exclusiveConverter, out int version)
    {
        if (exclusiveConverter is null) return TryGetDefaultConverterVersion(out version);
        if (exclusiveConverter is IYamlTypeConverter typed) return TryGetExclusiveConverterVersion(typed, out version);
        version = 0; return false;
    }
    public byte[] Compile(ReadOnlySpan<byte> text, CancellationToken cancellationToken) =>
        YamlBinaryCompiler.CompileYaml(text, cancellationToken);
}
