using BEngine.Hosting;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Serialization;

namespace BEngine.Editor.Build;

internal sealed class AssetBundlePlatformBuildTargetAdapter(
    IPlatformBuildTargetProvider provider, FileUtility files, PathUtility paths)
    : PlayerPlatformBuildTargetAdapter(provider, files, paths)
{
    protected override Func<string, IReadOnlyCollection<string>,
        IReadOnlyDictionary<string, PlatformPackagedAssemblySource>> PackagedAssemblySources =>
        static (resourceRoot, assemblyPaths) => AssetBundleManagedImageSources.Discover(resourceRoot, assemblyPaths);

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        var workspace = ProjectWorkspace.Open(context.Request.ProjectPath, FileSystem, Paths);
        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory, FileSystem, Paths);
        var executableName = Paths.GetFileName(layout.GetExecutablePath(context.Target.Platform));
        if (!context.Target.HasBuiltInPlayerHost) return;
        var playerResources = BuiltInResourceArchive.Open(
            layout.PlayerResourceArchivePath, FileSystem, Paths);
        var bootstrap = RuntimeMetadataSerializer.Deserialize(playerResources.ReadBytes(
                HostPackagedResourceAddresses.RuntimeMetadata)).PlayerBootstrap ??
            throw new InvalidDataException("The packaged runtime metadata has no Player bootstrap section.");
        var packagedTarget = BuildTargetManifestSerializer.Deserialize(playerResources.ReadBytes(
            HostPackagedResourceAddresses.BuildTargetManifest));
        if (!bootstrap.ProductName.Equals(workspace.Project.Name, StringComparison.Ordinal) ||
            !bootstrap.Executable.Equals(executableName, StringComparison.Ordinal) ||
            !packagedTarget.TargetId.Equals(context.Target.TargetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The platform provider output does not match the Player bootstrap manifest.");
        PlayerBuildOutputValidation.ValidateResourceDirectories(layout,
            [HostPackagedResourceAddresses.PlayerArchiveFileName, BuiltInResourceArchive.FileName], FileSystem, Paths);
    }


    protected override bool UsesHttpStreamingAssets(PlayerBuildContext context)
    {
        if (context.Target.Platform != BuildTargetPlatform.Web) return false;
        var settingsPath = Paths.Combine(context.Request.ProjectPath, "ProjectSettings", AssetBundleSettingsDocument.FileName);
        var settings = FileSystem.FileExists(settingsPath)
            ? YamlUtility.Load<AssetBundleSettingsDocument>(settingsPath, FileSystem, Paths) : null;
        if (settings is not { Enabled: true, LoadMode: AssetBundleLoadMode.StreamingAssets }) return false;
        var directory = settings.BuiltInDirectory.Replace('\\', '/').TrimEnd('/');
        if (!(directory.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase) ||
              directory.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Web direct AssetBundles must be placed under StreamingAssets.");
        return true;
    }
}
