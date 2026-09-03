using System.Text.Json;
using System.Text.Json.Serialization;
using BEngine.Serialization;

namespace BEngine.HotUpdate;

public sealed class ManagedCodeReleaseManifest
{
    public const string CurrentFormat = "BEngine.ManagedCodeRelease";
    public const int CurrentVersion = 1;
    public const string DefaultAddress = "Assets/__BEngine/HotUpdate/release.json";

    [JsonPropertyOrder(0)] public string Format { get; set; } = CurrentFormat;
    [JsonPropertyOrder(1)] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyOrder(2)] public string ReleaseId { get; set; } = string.Empty;
    [JsonPropertyOrder(3)] public int ContractVersion { get; set; } = HotUpdateContract.CurrentVersion;
    [JsonPropertyOrder(4)] public List<ManagedCodeModuleManifest> Modules { get; set; } = [];

    public void Validate()
    {
        if (!Format.Equals(CurrentFormat, StringComparison.Ordinal) || Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported managed-code release format '{Format}' version {Version}.");
        if (string.IsNullOrWhiteSpace(ReleaseId))
            throw new InvalidDataException("Managed-code release id is required.");
        if (ContractVersion <= 0)
            throw new InvalidDataException("Managed-code contract version must be positive.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in Modules)
        {
            module.Validate();
            if (!names.Add(module.Name))
                throw new InvalidDataException($"Duplicate managed-code module '{module.Name}'.");
        }
        foreach (var module in Modules)
        foreach (var dependency in module.Dependencies)
            if (!names.Contains(dependency))
                throw new InvalidDataException(
                    $"Managed-code module '{module.Name}' depends on missing module '{dependency}'.");
    }
}

public sealed class ManagedCodeModuleManifest
{
    public string Name { get; set; } = string.Empty;
    public string BuildId { get; set; } = string.Empty;
    public string AssemblyAddress { get; set; } = string.Empty;
    public string? SymbolsAddress { get; set; }
    public List<string> Dependencies { get; set; } = [];

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(BuildId) ||
            string.IsNullOrWhiteSpace(AssemblyAddress))
            throw new InvalidDataException("Managed-code module manifest is incomplete.");
        if (Dependencies.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Managed-code module '{Name}' has an empty dependency.");
    }
}

public static class ManagedCodeReleaseManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private static readonly BEngineRuntimeJsonSerializerContext JsonContext = new(Options);

    public static byte[] Serialize(ManagedCodeReleaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonContext.ManagedCodeReleaseManifest);
    }

    public static ManagedCodeReleaseManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        var manifest = JsonSerializer.Deserialize(bytes, JsonContext.ManagedCodeReleaseManifest) ??
                       throw new InvalidDataException("Managed-code release manifest is empty.");
        manifest.Validate();
        return manifest;
    }
}
