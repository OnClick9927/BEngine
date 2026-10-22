using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using BEngine.Platform;
using System.Text;
using YamlDotNet.Core;

namespace BEngine;

public static class YamlUtility
{
    private static readonly List<IYamlTypeConverter> Converters = [];
    private static ISerializer Serializer = CreateSerializer();
    private static ISerializer CreateSerializer() => Configure(new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .WithQuotingNecessaryStrings()
        .WithMaximumRecursion(64)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        )
        .Build();

    private static IDeserializer Deserializer = CreateDeserializer(false);
    private static IDeserializer StrictDeserializer = CreateDeserializer(true);
    private static IDeserializer CreateDeserializer(bool strict)
    {
        var builder = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .WithMaximumRecursion(64);
        return Configure(strict ? builder.WithEnforceNullability() : builder.IgnoreUnmatchedProperties()).Build();
    }

    private static SerializerBuilder Configure(SerializerBuilder builder) { foreach (var converter in Converters) builder.WithTypeConverter(converter); return builder; }
    private static DeserializerBuilder Configure(DeserializerBuilder builder) { foreach (var converter in Converters) builder.WithTypeConverter(converter); return builder; }
    public static void RegisterConverter(IYamlTypeConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        lock (typeof(YamlUtility)) { Converters.RemoveAll(c => c.GetType() == converter.GetType()); Converters.Add(converter); Rebuild(); }
    }
    public static void UnregisterConverters(System.Reflection.Assembly assembly)
    {
        lock (typeof(YamlUtility)) { if (Converters.RemoveAll(c => c.GetType().Assembly == assembly) > 0) Rebuild(); }
    }
    private static void Rebuild() { Serializer = CreateSerializer(); Deserializer = CreateDeserializer(false); StrictDeserializer = CreateDeserializer(true); }
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static byte[] SerializeToUtf8Bytes(object value) => Utf8.GetBytes(Serialize(value));

    public static T Deserialize<T>(ReadOnlySpan<byte> yaml, bool strict = false)
    {
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
        return Serializer.Serialize(value).ReplaceLineEndings("\n");
    }

    public static T Deserialize<T>(string yaml, bool strict = false) =>
        (T)Deserialize(yaml, typeof(T), strict);

    public static object Deserialize(string yaml, Type type, bool strict = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        ArgumentNullException.ThrowIfNull(type);
        try
        {
            var value = (strict ? StrictDeserializer : Deserializer).Deserialize(new StringReader(yaml), type) ??
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
