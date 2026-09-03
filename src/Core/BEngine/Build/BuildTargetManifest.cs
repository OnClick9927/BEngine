using System.Text.Json;
using System.Text.Json.Serialization;
using BEngine.HotUpdate;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;

namespace BEngine.Build;

public sealed class BuildTargetManifest
{
    public const string CurrentFormat = "BEngine.BuildTarget";
    public const int CurrentVersion = 1;
    public const string FileName = "BEngine.BuildTarget.json";

    [JsonPropertyOrder(0)] public string Format { get; set; } = CurrentFormat;
    [JsonPropertyOrder(1)] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyOrder(2)] public string TargetId { get; set; } = string.Empty;
    [JsonPropertyOrder(3)] public BuildTargetPlatform Platform { get; set; }
    [JsonPropertyOrder(4)] public BuildArchitecture Architecture { get; set; }
    [JsonPropertyOrder(5)] public string RuntimeIdentifier { get; set; } = string.Empty;
    [JsonPropertyOrder(6)] public ManagedCodeRuntimeKind ManagedCodeRuntime { get; set; }
    [JsonPropertyOrder(7)] public List<GraphicsBackend> GraphicsBackends { get; set; } = [];

    public static BuildTargetManifest FromDescriptor(
        BuildTargetDescriptor descriptor,
        IEnumerable<GraphicsBackend>? graphicsBackends = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new BuildTargetManifest
        {
            TargetId = descriptor.TargetId,
            Platform = descriptor.Platform,
            Architecture = descriptor.Architecture,
            RuntimeIdentifier = descriptor.RuntimeIdentifier,
            ManagedCodeRuntime = descriptor.ManagedCodeRuntime,
            GraphicsBackends = (graphicsBackends ?? descriptor.GraphicsBackends).ToList()
        };
    }

    public void Validate()
    {
        if (!Format.Equals(CurrentFormat, StringComparison.Ordinal) || Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported build-target format '{Format}' version {Version}.");
        var descriptor = BuildTargetCatalog.Get(TargetId);
        if (descriptor.Platform != Platform || descriptor.Architecture != Architecture ||
            !descriptor.RuntimeIdentifier.Equals(RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) ||
            descriptor.ManagedCodeRuntime != ManagedCodeRuntime)
            throw new InvalidDataException($"Build-target manifest '{TargetId}' does not match the target catalog.");
        if (GraphicsBackends.Count == 0 || GraphicsBackends.Any(backend => backend == GraphicsBackend.Auto))
            throw new InvalidDataException("Build-target manifest requires concrete graphics backends.");
        if (GraphicsBackends.Count != GraphicsBackends.Distinct().Count())
            throw new InvalidDataException("Build-target manifest contains duplicate graphics backends.");
        var expectedOrder = descriptor.GraphicsBackends
            .Where(GraphicsBackends.Contains)
            .ToArray();
        if (!GraphicsBackends.SequenceEqual(expectedOrder))
            throw new InvalidDataException(
                $"Build-target manifest '{TargetId}' graphics backends must be an ordered subset of the target catalog.");
    }
}

public static class BuildTargetManifestSerializer
{
    public const string CurrentManifestPathAppContextKey = "BEngine.BuildTarget.ManifestPath";
    public const string CurrentManifestBytesAppContextKey = "BEngine.BuildTarget.ManifestBytes";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter<BuildTargetPlatform>(),
            new JsonStringEnumConverter<BuildArchitecture>(),
            new JsonStringEnumConverter<ManagedCodeRuntimeKind>(),
            new JsonStringEnumConverter<GraphicsBackend>()
        }
    };
    private static readonly BEngineRuntimeJsonSerializerContext JsonContext = new(Options);

    public static byte[] Serialize(BuildTargetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonContext.BuildTargetManifest);
    }

    public static BuildTargetManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        var manifest = JsonSerializer.Deserialize(bytes, JsonContext.BuildTargetManifest) ??
                       throw new InvalidDataException("Build-target manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    public static BuildTargetManifest LoadCurrent(string? baseDirectory = null)
    {
        if (AppContext.GetData(CurrentManifestBytesAppContextKey) is byte[] configuredBytes)
            return Deserialize(configuredBytes);
        var configuredPath = AppContext.GetData(CurrentManifestPathAppContextKey) as string;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var requiredPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(requiredPath))
                throw new FileNotFoundException(
                    "The platform Player Host build-target manifest was not found.", requiredPath);
            return Deserialize(File.ReadAllBytes(requiredPath));
        }
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, BuildTargetManifest.FileName);
        return File.Exists(path)
            ? Deserialize(File.ReadAllBytes(path))
            : BuildTargetManifest.FromDescriptor(BuildTargetCatalog.InferCurrentDesktop());
    }

    public static void SetCurrent(ReadOnlySpan<byte> bytes)
    {
        var snapshot = bytes.ToArray();
        _ = Deserialize(snapshot);
        AppContext.SetData(CurrentManifestPathAppContextKey, null);
        AppContext.SetData(CurrentManifestBytesAppContextKey, snapshot);
    }

    public static void SetCurrentPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                "The platform Player Host build-target manifest was not found.", fullPath);
        _ = Deserialize(File.ReadAllBytes(fullPath));
        AppContext.SetData(CurrentManifestBytesAppContextKey, null);
        AppContext.SetData(CurrentManifestPathAppContextKey, fullPath);
    }

    public static void Save(BuildTargetManifest manifest, string directory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(Path.GetFullPath(directory), BuildTargetManifest.FileName),
            Serialize(manifest));
    }
}
