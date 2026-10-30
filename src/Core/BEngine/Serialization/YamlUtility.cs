using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using BEngine.Platform;
using System.Text;
using YamlDotNet.Core;

namespace BEngine;

public static class YamlUtility
{
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
        builder.WithTypeConverter(Fix64YamlConverter.Instance);
        foreach (var converter in converters) builder.WithTypeConverter(converter);
        return builder;
    }
    private static DeserializerBuilder Configure(DeserializerBuilder builder, IYamlTypeConverter[] converters)
    {
        builder.WithTypeConverter(Fix64YamlConverter.Instance);
        foreach (var converter in converters) builder.WithTypeConverter(converter);
        return builder;
    }
    public static void RegisterConverter(IYamlTypeConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        lock (typeof(YamlUtility))
        {
            var current = Codecs;
            PublishConverters(current.Converters.Where(c => c.GetType() != converter.GetType()).Append(converter).ToArray(),
                unchecked(current.Version + 1));
        }
    }
    public static void UnregisterConverters(System.Reflection.Assembly assembly)
    {
        lock (typeof(YamlUtility))
        {
            var current = Codecs;
            var remaining = current.Converters.Where(c => c.GetType().Assembly != assembly).ToArray();
            if (remaining.Length != current.Converters.Length)
                PublishConverters(remaining, unchecked(current.Version + 1));
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
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static byte[] SerializeToUtf8Bytes(object value) => Utf8.GetBytes(Serialize(value));

    public static T Deserialize<T>(ReadOnlySpan<byte> yaml, bool strict = false)
    {
        if (BinaryDataSerializer.IsBinaryData(yaml)) return BinaryDataSerializer.Deserialize<T>(yaml, strict);
        try { return Deserialize<T>(Utf8.GetString(yaml), strict); }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The YAML document is not valid UTF-8.", exception);
        }
    }

    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BEngine.SerializationCallbackUtility.BeforeSerialize(value);
        return Volatile.Read(ref Codecs).Serializer.Value.Serialize(value).ReplaceLineEndings("\n");
    }

    public static T Deserialize<T>(string yaml, bool strict = false) =>
        (T)Deserialize(yaml, typeof(T), strict);

    public static object Deserialize(string yaml, Type type, bool strict = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        ArgumentNullException.ThrowIfNull(type);
        try
        {
            var codecs = Volatile.Read(ref Codecs);
            var value = (strict ? codecs.StrictDeserializer : codecs.Deserializer).Value.Deserialize(new StringReader(yaml), type) ??
                        throw new InvalidDataException($"The YAML document for {type.Name} is empty.");
            BEngine.SerializationCallbackUtility.AfterDeserialize(value);
            return value;
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"The YAML document for {type.Name} is invalid: {exception.Message}", exception);
        }
    }

    public static T Load<T>(string path) =>
        Load<T>(path, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static T Load<T>(string path, FileUtility fileSystem, PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullPath = pathService.GetFullPath(path);
        return Deserialize<T>(fileSystem.ReadAllText(fullPath));
    }

    public static object Load(string path, Type type) =>
        Load(path, type, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static object Load(
        string path,
        Type type,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullPath = pathService.GetFullPath(path);
        return Deserialize(fileSystem.ReadAllText(fullPath), type);
    }

    public static void Save(object value, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (value is BObject { IsRuntimeOnly: true })
            throw new InvalidOperationException("Runtime objects are transient and cannot be saved.");

        var fileSystem = RuntimeServiceAccess.FileSystem;
        var paths = RuntimeServiceAccess.PathService;
        var fullPath = paths.GetFullPath(path);
        var directory = paths.GetDirectoryName(fullPath) ?? RuntimeServiceAccess.PlatformService.CurrentDirectory;
        fileSystem.CreateDirectory(directory);
        var temporaryPath = paths.Combine(directory, $".{paths.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            fileSystem.WriteAllText(temporaryPath, Serialize(value));
            fileSystem.MoveFile(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (fileSystem.FileExists(temporaryPath)) fileSystem.DeleteFile(temporaryPath);
        }
    }
}
