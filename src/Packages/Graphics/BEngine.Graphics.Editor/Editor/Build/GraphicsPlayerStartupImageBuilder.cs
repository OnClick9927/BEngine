using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

internal sealed class GraphicsPlayerStartupImageBuilder : IPlayerStartupImageBuilder
{
    [ModuleInitializer]
    internal static void Register() => RuntimeServiceContributions.RegisterDefault<IPlayerStartupImageBuilder>(new GraphicsPlayerStartupImageBuilder());

    public PlayerStartupImages Build(ProjectWorkspace workspace, PlayerBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        var fileSystem = FileUtility.Current;
        var pathService = PathUtility.Current;
        var platformService = PlatformUtility.Current;
        var source = string.IsNullOrWhiteSpace(request.SplashImage)
            ? ResolveDefaultSplashImage(fileSystem, pathService, platformService)
            : workspace.ResolveInside(request.SplashImage.Replace('/', pathService.DirectorySeparatorChar));
        if (!fileSystem.FileExists(source))
            throw new FileNotFoundException("The configured Player splash image was not found.", source);
        if (!pathService.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Player splash image must be a PNG file.");
        var bytes = fileSystem.ReadAllBytes(source);
        if (!BEngine.Rendering.PngImageCodec.TryDecode(bytes, out var width, out var height, out _) ||
            width > 8192 || height > 8192)
            throw new InvalidDataException(
                $"The Player splash image is not a supported PNG or exceeds 8192x8192: '{source}'.");
        var applicationIcon = bytes;
        if (!string.IsNullOrWhiteSpace(request.SplashImage))
        {
            var engineIconPath = ResolveDefaultSplashImage(fileSystem, pathService, platformService);
            var engineIcon = fileSystem.ReadAllBytes(engineIconPath);
            var badgePath = pathService.Combine(pathService.GetDirectoryName(engineIconPath)!, "BEngine.Badge.png");
            var badge = fileSystem.FileExists(badgePath) ? fileSystem.ReadAllBytes(badgePath) : engineIcon;
            bytes = PlayerSplashImageComposer.AddEngineBadge(bytes, engineIcon, badge);
        }
        return new PlayerStartupImages(bytes, applicationIcon);
    }

    private static string ResolveDefaultSplashImage(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        // The default image belongs to the drawing module, not the engine installation layout.
        foreach (var package in BEngine.ProjectSystem.BPackageCatalog.DiscoverAvailable().packages)
        {
            if (package.Document.Runtime?.Assembly != typeof(Texture).Assembly.GetName().Name) continue;
            var candidate = pathService.Combine(
                pathService.GetDirectoryName(package.Path)!, package.Document.Content.Runtime, "Icons", "BEngine.png");
            if (fileSystem.FileExists(candidate)) return candidate;
        }
        if (EditorResource.FindPath("Icons/BEngine.png") is { } registered) return registered;
        foreach (var start in new[] { platformService.BaseDirectory, platformService.CurrentDirectory })
        for (var directory = pathService.GetFullPath(start); !string.IsNullOrWhiteSpace(directory);
             directory = pathService.GetDirectoryName(directory))
        {
            foreach (var candidate in new[]
                     {
                         pathService.Combine(directory, "Editor", "Icons", "BEngine.png"),
                     })
                if (fileSystem.FileExists(candidate)) return candidate;

            var parent = pathService.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(parent) || parent.Equals(directory, StringComparison.OrdinalIgnoreCase))
                break;
        }
        throw new FileNotFoundException(
            "The default BEngine splash image was not found. Re-export the engine or configure an Assets/*.png image.");
    }

}
