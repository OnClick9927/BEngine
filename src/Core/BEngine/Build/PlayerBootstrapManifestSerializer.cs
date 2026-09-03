using System.Text.Json;
using System.Text.Json.Serialization;
using BEngine.Serialization;

namespace BEngine.Build;

public static class PlayerBootstrapManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private static readonly BEngineRuntimeJsonSerializerContext JsonContext = new(Options);

    public static byte[] Serialize(PlayerBootstrapManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonContext.PlayerBootstrapManifest);
    }

    public static PlayerBootstrapManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        var manifest = JsonSerializer.Deserialize(bytes, JsonContext.PlayerBootstrapManifest) ??
                       throw new InvalidDataException("Player bootstrap manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    public static PlayerBootstrapManifest Load(string playerDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        var path = Path.Combine(Path.GetFullPath(playerDirectory), PlayerBootstrapManifest.FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("The Player bootstrap manifest was not found.", path);
        return Deserialize(File.ReadAllBytes(path));
    }

    public static void Save(PlayerBootstrapManifest manifest, string playerDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        Directory.CreateDirectory(playerDirectory);
        File.WriteAllBytes(Path.Combine(Path.GetFullPath(playerDirectory), PlayerBootstrapManifest.FileName),
            Serialize(manifest));
    }
}
