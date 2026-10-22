using YamlDotNet.Serialization;
using BEngine.Platform;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;

namespace BEngine.Build;

public sealed class BuildTargetManifest
{
    public const string CurrentFormat = "BEngine.BuildTarget";
    public const int CurrentVersion = 1;
    public const string FileName = "BEngine.BuildTarget.yaml";

    [YamlMember(Order = 0)] public string Format { get; set; } = CurrentFormat;
    [YamlMember(Order = 1)] public int Version { get; set; } = CurrentVersion;
    [YamlMember(Order = 2)] public string TargetId { get; set; } = string.Empty;
    [YamlMember(Order = 3)] public BuildTargetPlatform Platform { get; set; }
    [YamlMember(Order = 4)] public BuildArchitecture Architecture { get; set; }
    [YamlMember(Order = 5)] public string RuntimeIdentifier { get; set; } = string.Empty;
    [YamlMember(Order = 6)] public ManagedCodeRuntimeKind ManagedCodeRuntime { get; set; }
    [YamlMember(Order = 7)] public List<GraphicsBackend> GraphicsBackends { get; set; } = [];

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
        var descriptor = BEngine.Platform.BuildTargetUtility.Current.Get(TargetId);
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

    public static byte[] Serialize(BuildTargetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        return YamlUtility.SerializeToUtf8Bytes(manifest);
    }

    public static BuildTargetManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        var manifest = YamlUtility.Deserialize<BuildTargetManifest>(bytes, strict: true);
        manifest.Validate();
        return manifest;
    }

    public static BuildTargetManifest LoadCurrent(string? baseDirectory = null) =>
        LoadCurrent(
            baseDirectory,
            RuntimeServiceAccess.FileSystem,
            RuntimeServiceAccess.PathService,
            RuntimeServiceAccess.PlatformService);

    public static BuildTargetManifest LoadCurrent(
        string? baseDirectory,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        if (platformService.GetRuntimeData(CurrentManifestBytesAppContextKey) is byte[] configuredBytes)
            return Deserialize(configuredBytes);
        var configuredPath = platformService.GetRuntimeData(CurrentManifestPathAppContextKey) as string;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var requiredPath = pathService.GetFullPath(configuredPath);
            if (!fileSystem.FileExists(requiredPath))
                throw new FileNotFoundException(
                    "The platform Player Host build-target manifest was not found.", requiredPath);
            return Deserialize(fileSystem.ReadAllBytes(requiredPath));
        }
        var path = pathService.Combine(
            baseDirectory ?? platformService.BaseDirectory, BuildTargetManifest.FileName);
        return fileSystem.FileExists(path)
            ? Deserialize(fileSystem.ReadAllBytes(path))
            : BuildTargetManifest.FromDescriptor(BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktop(platformService));
    }

    public static void SetCurrent(ReadOnlySpan<byte> bytes)
    {
        var snapshot = bytes.ToArray();
        _ = Deserialize(snapshot);
        BEngine.Platform.PlatformUtility.Current.SetRuntimeData(CurrentManifestPathAppContextKey, null);
        BEngine.Platform.PlatformUtility.Current.SetRuntimeData(CurrentManifestBytesAppContextKey, snapshot);
    }

    public static void SetCurrentPath(string path) =>
        SetCurrentPath(path, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static void SetCurrentPath(
        string path,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullPath = pathService.GetFullPath(path);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException(
                "The platform Player Host build-target manifest was not found.", fullPath);
        _ = Deserialize(fileSystem.ReadAllBytes(fullPath));
        BEngine.Platform.PlatformUtility.Current.SetRuntimeData(CurrentManifestBytesAppContextKey, null);
        BEngine.Platform.PlatformUtility.Current.SetRuntimeData(CurrentManifestPathAppContextKey, fullPath);
    }

    public static void Save(BuildTargetManifest manifest, string directory) =>
        Save(manifest, directory, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static void Save(
        BuildTargetManifest manifest,
        string directory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        fileSystem.CreateDirectory(directory);
        fileSystem.WriteAllBytes(pathService.Combine(
                pathService.GetFullPath(directory), BuildTargetManifest.FileName),
            Serialize(manifest));
    }
}
