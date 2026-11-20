using System.Text;
using System.Text.Json;

namespace BEngine.Hosting;

/// <summary>Portable startup environment supplied by a launcher, without feature-specific arguments.</summary>
public static class HostLaunchEnvironment
{
    public const string ArgumentName = "bengineEnvironment";
    private const int MaximumPayloadBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Encodes plain values and optional paths relative to the host's private file directory.</summary>
    public static string Encode(IReadOnlyDictionary<string, string> environment,
        IReadOnlyDictionary<string, string>? relativeFiles = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (var pair in environment) Validate(pair.Key, pair.Value, false);
        if (relativeFiles is not null)
            foreach (var pair in relativeFiles)
            {
                Validate(pair.Key, pair.Value, true);
                if (environment.ContainsKey(pair.Key))
                    throw new InvalidDataException("A launch variable cannot have both a value and a relative file path.");
            }
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            WriteMap(writer, "environment", environment);
            if (relativeFiles is not null) WriteMap(writer, "relativeFiles", relativeFiles);
            writer.WriteEndObject();
        }
        if (output.Length > MaximumPayloadBytes) throw new InvalidDataException("Launch environment is too large.");
        return Convert.ToBase64String(output.GetBuffer(), 0, checked((int)output.Length))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Validates the entire payload before returning any values. Does not mutate process state.</summary>
    public static IReadOnlyDictionary<string, string> Decode(string encoded, string privateFileDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(encoded);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateFileDirectory);
        if (encoded.Length > MaximumPayloadBytes * 2 || encoded.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new InvalidDataException("Invalid encoded launch environment.");
        var base64 = encoded.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException exception) { throw new InvalidDataException("Invalid encoded launch environment.", exception); }
        if (bytes.Length > MaximumPayloadBytes) throw new InvalidDataException("Launch environment is too large.");
        JsonDocument document;
        try { document = JsonDocument.Parse(Utf8.GetString(bytes), new JsonDocumentOptions { MaxDepth = 8 }); }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        { throw new InvalidDataException("Invalid launch environment JSON.", exception); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetDouble(out var number) || number != 1)
                throw new InvalidDataException("Unsupported launch environment version.");
            foreach (var property in root.EnumerateObject())
                if (property.Name is not ("version" or "environment" or "relativeFiles"))
                    throw new InvalidDataException("Unknown launch environment field.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            ReadMap("environment", false);
            ReadMap("relativeFiles", true);
            return result;

            void ReadMap(string name, bool relativeFile)
            {
                if (!root.TryGetProperty(name, out var map)) return;
                if (map.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Launch environment fields must be objects.");
                foreach (var property in map.EnumerateObject())
                {
                    // JSON.parse in the browser retains the final duplicate property.
                    var item = map.GetProperty(property.Name);
                    if (item.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Launch environment values must be strings.");
                    var value = item.GetString()!;
                    Validate(property.Name, value, relativeFile);
                    if (relativeFile && root.TryGetProperty("environment", out var values) &&
                        values.TryGetProperty(property.Name, out _))
                        throw new InvalidDataException("A launch variable cannot have both a value and a relative file path.");
                    result[property.Name] = relativeFile
                        ? Path.GetFullPath(Path.Combine(privateFileDirectory,
                            value.Replace('/', Path.DirectorySeparatorChar))) : value;
                }
            }
        }
    }

    public static void Apply(string encoded, string privateFileDirectory)
    {
        foreach (var pair in Decode(encoded, privateFileDirectory))
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private static void WriteMap(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, string> values)
    {
        writer.WriteStartObject(name);
        foreach (var pair in values) writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }

    private static void Validate(string name, string value, bool relativeFile)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_') ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidDataException("Invalid launch environment variable name.");
        if (value is null || value.Contains('\0')) throw new InvalidDataException("Invalid launch environment value.");
        if (!relativeFile) return;
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => character < ' ' || "\\:<>\"|?*".Contains(character)) ||
            value.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Launch file paths must be portable relative paths without traversal.");
    }
}
