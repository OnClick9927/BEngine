using BEngine.Platform;
using System.Text;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace BEngine;

/// <summary>Stable document API; text parsing is supplied by the host's codec.</summary>
public static class YamlUtility
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal static bool TryGetDefaultConverterVersion(out int version)
    {
        var codec = TextDataSerialization.Codec;
        if (codec is not null) return codec.TryGetConverterVersion(null, out version);
        version = 0; return true;
    }
    /// <summary>Reports whether the expected converter is the sole extension override, together
    /// with its generation. Compiled-data adapters must check the generation again after conversion.</summary>
    public static bool TryGetExclusiveConverterVersion(object expected, out int version)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var codec = TextDataSerialization.Codec;
        if (codec is not null) return codec.TryGetConverterVersion(expected, out version);
        version = 0; return false;
    }
    public static void RegisterConverter<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicMethods)] T>(T converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        TextDataSerialization.RequireCodec().RegisterConverter(converter);
    }
    public static void UnregisterConverters(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        TextDataSerialization.Codec?.UnregisterConverters(assembly);
    }
    public static byte[] SerializeToUtf8Bytes(object value) => Utf8.GetBytes(Serialize(value));
    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SerializationCallbackUtility.BeforeSerialize(value);
        return TextDataSerialization.RequireCodec().Serialize(value);
    }
    public static T Deserialize<T>(ReadOnlySpan<byte> yaml, bool strict = false)
    {
        if (BinaryDataSerializer.IsBinaryData(yaml)) return BinaryDataSerializer.Deserialize<T>(yaml, strict);
        try { return Deserialize<T>(Utf8.GetString(yaml), strict); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("The YAML document is not valid UTF-8.", exception); }
    }
    public static T Deserialize<T>(string yaml, bool strict = false) => (T)Deserialize(yaml, typeof(T), strict);
    public static object Deserialize(string yaml, Type type, bool strict = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        ArgumentNullException.ThrowIfNull(type);
        var value = TextDataSerialization.RequireCodec().Deserialize(yaml, type, strict);
        SerializationCallbackUtility.AfterDeserialize(value);
        return value;
    }
    public static T Load<T>(string path) =>
        Load<T>(path, RuntimeServices.FileSystem, RuntimeServices.PathService);

    public static T Load<T>(string path, FileUtility fileSystem, PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullPath = pathService.GetFullPath(path);
        return Deserialize<T>(fileSystem.ReadAllText(fullPath));
    }

    public static object Load(string path, Type type) =>
        Load(path, type, RuntimeServices.FileSystem, RuntimeServices.PathService);

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

        var fileSystem = RuntimeServices.FileSystem;
        var paths = RuntimeServices.PathService;
        var fullPath = paths.GetFullPath(path);
        var directory = paths.GetDirectoryName(fullPath) ?? RuntimeServices.PlatformService.CurrentDirectory;
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
