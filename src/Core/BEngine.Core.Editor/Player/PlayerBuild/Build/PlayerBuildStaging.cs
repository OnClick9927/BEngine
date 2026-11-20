using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

internal static class PlayerBuildStaging
{
    internal static void Complete(PlayerBuildLayout layout, FileUtility files)
    {
        foreach (var path in new[] { layout.BuildTargetManifestStagingPath, layout.SplashImageStagingPath })
            if (files.FileExists(path)) files.DeleteFile(path);
    }

    internal static void Prepare(ProjectWorkspace workspace, PlayerBuildLayout layout, PlayerBuildContext context,
        FileUtility files, PathUtility paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        layout.CreateDataDirectories();
        PlayerStreamingAssetsBuilder.Copy(workspace, layout, files, paths, cancellationToken);
        BuildTargetManifestSerializer.Save(BuildTargetManifest.FromDescriptor(context.Target,
            context.Request.GraphicsBackends), layout.PlayerResourceStagingDirectory, files, paths);
        var splashResource = string.Empty;
        if (context.Request.SplashScreenEnabled)
        {
            var builder = RuntimeServiceContributions.GetDefault<IPlayerStartupImageBuilder>() ??
                throw new InvalidOperationException("No Player startup image builder is registered.");
            var images = builder.Build(workspace, context.Request);
            if (images.LogoPng.IsEmpty)
                throw new InvalidDataException("The Player startup image builder returned an empty logo.");
            context.StartupLogoPng = images.LogoPng;
            context.ApplicationIconPng = images.ApplicationIconPng;
            files.WriteAllBytes(layout.SplashImageStagingPath, images.LogoPng.ToArray());
            splashResource = HostPackagedResourceAddresses.SplashImage;
        }
        else
        {
            context.StartupLogoPng = ReadOnlyMemory<byte>.Empty;
            context.ApplicationIconPng = ReadOnlyMemory<byte>.Empty;
        }
        var request = context.Request;
        context.DeploymentManifest = new HostDeploymentManifest
        {
            ProductName = workspace.Project.Name,
            BuildVersion = request.BuildVersion,
            DevelopmentBuild = request.DevelopmentBuild,
            WritePlayerLog = request.WritePlayerLog,
            Executable = context.Target.Platform == BuildTargetPlatform.Windows ? $"{layout.PlayerName}.exe" : layout.PlayerName,
            DataDirectory = layout.DataDirectoryName,
            ResourceDirectory = layout.DataDirectoryName,
            NativeLibraryDirectory = $"{layout.DataDirectoryName}/" + HostPackagedResourceAddresses.NativeLibraryDirectoryName,
            PlayerResourceArchive = $"{layout.DataDirectoryName}/" + HostPackagedResourceAddresses.ResourcesDirectoryName + "/" + HostPackagedResourceAddresses.PlayerArchiveFileName,
            BuildTargetManifest = string.Empty,
            BuildTargetResource = HostPackagedResourceAddresses.BuildTargetManifest,
            SplashScreenEnabled = request.SplashScreenEnabled,
            SplashImage = string.Empty,
            SplashImageResource = splashResource,
            SplashBackgroundColor = request.SplashBackgroundColor,
            SplashMinimumDurationSeconds = request.SplashMinimumDurationSeconds,
            CacheDirectory = request.CacheDirectory,
            ApplicationScene = request.HotUpdateStartupScene
        };
        cancellationToken.ThrowIfCancellationRequested();
    }
}
