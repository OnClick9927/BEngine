using BEngine.Build;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Hosting;

public static class HostDeploymentManifestSerializer
{

    public static byte[] Serialize(HostDeploymentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        return YamlUtility.SerializeToUtf8Bytes(manifest);
    }

    public static HostDeploymentManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        var manifest = YamlUtility.Deserialize<HostDeploymentManifest>(bytes, strict: true);
        manifest.Validate();
        return manifest;
    }

    public static HostDeploymentManifest Load(string playerDirectory) =>
        Load(playerDirectory, RuntimeServices.FileSystem, RuntimeServices.PathService);

    public static HostDeploymentManifest Load(
        string playerDirectory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var path = pathService.Combine(
            pathService.GetFullPath(playerDirectory), HostDeploymentManifest.FileName);
        if (!fileSystem.FileExists(path))
            throw new FileNotFoundException("The Player bootstrap manifest was not found.", path);
        return Deserialize(fileSystem.ReadAllBytes(path));
    }

    public static void Save(HostDeploymentManifest manifest, string playerDirectory) =>
        Save(
            manifest,
            playerDirectory,
            RuntimeServices.FileSystem,
            RuntimeServices.PathService);

    public static void Save(
        HostDeploymentManifest manifest,
        string playerDirectory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        fileSystem.CreateDirectory(playerDirectory);
        fileSystem.WriteAllBytes(pathService.Combine(
                pathService.GetFullPath(playerDirectory), HostDeploymentManifest.FileName),
            Serialize(manifest));
    }
}
