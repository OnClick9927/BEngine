using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BEngine.Serialization;

public static class YamlUtility
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Serializer.Serialize(value);
    }

    public static T Deserialize<T>(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        return Deserializer.Deserialize<T>(yaml) ??
            throw new InvalidDataException($"The YAML document for {typeof(T).Name} is empty.");
    }

    public static T Load<T>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize<T>(File.ReadAllText(Path.GetFullPath(path)));
    }

    public static void Save(object value, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

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
