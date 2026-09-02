using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BEngine;

public static class YamlUtility
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithTypeConverter(new TextureAtlasYamlConverter())
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .WithTypeConverter(new TextureAtlasYamlConverter())
        .Build();

    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BEngine.SerializationCallbackUtility.BeforeSerialize(value);
        return Serializer.Serialize(value);
    }

    public static T Deserialize<T>(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        var value = Deserializer.Deserialize<T>(yaml) ??
                    throw new InvalidDataException($"The YAML document for {typeof(T).Name} is empty.");
        BEngine.SerializationCallbackUtility.AfterDeserialize(value);
        return value;
    }

    public static object Deserialize(string yaml, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        ArgumentNullException.ThrowIfNull(type);
        var value = Deserializer.Deserialize(new StringReader(yaml), type) ??
                    throw new InvalidDataException($"The YAML document for {type.Name} is empty.");
        BEngine.SerializationCallbackUtility.AfterDeserialize(value);
        return value;
    }

    public static T Load<T>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize<T>(File.ReadAllText(Path.GetFullPath(path)));
    }

    public static object Load(string path, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllText(Path.GetFullPath(path)), type);
    }

    public static void Save(object value, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (value is BObject { IsRuntimeOnly: true })
            throw new InvalidOperationException("Runtime objects are transient and cannot be saved.");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, Serialize(value));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
