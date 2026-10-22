using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Build;

public static class PlayerBootstrapManifestSerializer
{

    public static byte[] Serialize(PlayerBootstrapManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        return YamlUtility.SerializeToUtf8Bytes(manifest);
    }

    public static PlayerBootstrapManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        var manifest = YamlUtility.Deserialize<PlayerBootstrapManifest>(bytes, strict: true);
        manifest.Validate();
        return manifest;
    }

    public static PlayerBootstrapManifest Load(string playerDirectory) =>
        Load(playerDirectory, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static PlayerBootstrapManifest Load(
        string playerDirectory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var path = pathService.Combine(
            pathService.GetFullPath(playerDirectory), PlayerBootstrapManifest.FileName);
        if (!fileSystem.FileExists(path))
            throw new FileNotFoundException("The Player bootstrap manifest was not found.", path);
        return Deserialize(fileSystem.ReadAllBytes(path));
    }

    public static void Save(PlayerBootstrapManifest manifest, string playerDirectory) =>
        Save(
            manifest,
            playerDirectory,
            RuntimeServiceAccess.FileSystem,
            RuntimeServiceAccess.PathService);

    public static void Save(
        PlayerBootstrapManifest manifest,
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
                pathService.GetFullPath(playerDirectory), PlayerBootstrapManifest.FileName),
            Serialize(manifest));
    }
}
