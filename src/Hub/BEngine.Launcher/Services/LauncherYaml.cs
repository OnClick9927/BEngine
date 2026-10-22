using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BEngine.Launcher;

// Hub persists only plain project/package/preferences documents. It does not
// load runtime objects or register asset serialization converters.
internal static class YamlUtility
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).DisableAliases()
        .WithQuotingNecessaryStrings().WithMaximumRecursion(64)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build();
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).WithDuplicateKeyChecking()
        .WithMaximumRecursion(64).IgnoreUnmatchedProperties().Build();
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Serialize(object value) => Serializer.Serialize(value).ReplaceLineEndings("\n");

    internal static T Deserialize<T>(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        try
        {
            return Deserializer.Deserialize<T>(yaml) ??
                   throw new InvalidDataException($"The YAML document for {typeof(T).Name} is empty.");
        }
        catch (YamlException exception)
        { throw new InvalidDataException($"The YAML document for {typeof(T).Name} is invalid: {exception.Message}", exception); }
    }

    internal static T Load<T>(string path)
    {
        try { return Deserialize<T>(File.ReadAllText(Path.GetFullPath(path), Utf8)); }
        catch (DecoderFallbackException exception)
        { throw new InvalidDataException("The YAML document is not valid UTF-8.", exception); }
    }

    internal static void Save(object value, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, Serialize(value), Utf8);
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
