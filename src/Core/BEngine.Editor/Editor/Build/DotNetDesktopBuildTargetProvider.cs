using BEngine.Build;
using BEngine.Content;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

public sealed class DotNetDesktopBuildTargetProvider :
    IPlayerBuildTargetProvider,
    IPlayerBuildPrerequisiteProvider
{
    public string ProviderId => "bengine.dotnet-desktop";

    public bool SupportsTarget(BuildTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.HasBuiltInPlayerHost;
    }

    public IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(BuildTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SupportsTarget(target)) return [];
        var environment = DotNetBuildEnvironmentProbe.Inspect();
        var hostFound = DotNetBuildHostLocator.TryResolve(target, null, out _);
        return
        [
            new PlayerBuildPrerequisite(
                "dotnet-sdk",
                environment.DotNetAvailable,
                environment.DotNetAvailable
                    ? $".NET SDK {environment.SdkVersion} is available."
                    : $"The .NET SDK is unavailable. {environment.Diagnostic}".Trim(),
                "Install the .NET 10 SDK and ensure 'dotnet' is on PATH."),
            new PlayerBuildPrerequisite(
                "desktop-player-host",
                hostFound,
                hostFound
                    ? "The desktop Player Host source or exported BuildHost template is available."
                    : "The desktop Player Host source and exported BuildHost template are both missing.",
                "Re-export BEngine with the BuildHosts directory, or set PlayerHostProjectPath.")
        ];
    }

    public bool CanBuild(BuildTargetDescriptor target, out string reason)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SupportsTarget(target))
        {
            reason = $"'{target.TargetId}' is not a desktop target.";
            return false;
        }
        var failed = GetPrerequisites(target).Where(item => !item.IsSatisfied).ToArray();
        reason = string.Join(" ", failed.Select(item =>
            $"{item.Message}{(string.IsNullOrWhiteSpace(item.Remediation) ? string.Empty : $" {item.Remediation}")}"));
        return failed.Length == 0;
    }

    public async Task BuildAsync(PlayerBuildContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanBuild(context.Target, out var reason)) throw new PlatformNotSupportedException(reason);
        var playerProject = DotNetBuildHostLocator.Resolve(
            context.Target, context.Request.PlayerHostProjectPath);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.CompilingPlayer,
            $"Publishing Player for {context.Target.RuntimeIdentifier}", 0.65f));
        var workspace = ProjectWorkspace.Open(context.Request.ProjectPath);
        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory);
        layout.CreateDataDirectories();
        var executablePath = layout.GetExecutablePath(context.Target.Platform);
        var executableName = Path.GetFileName(executablePath);

        var publishDirectory = Path.Combine(context.StagingDirectory, ".bengine-player-publish");
        Directory.CreateDirectory(publishDirectory);
        try
        {
            await DotNetPublishRunner.RunAsync(playerProject, context, start =>
            {
                start.ArgumentList.Add("-p:UseAppHost=true");
                start.ArgumentList.Add("-p:PublishSingleFile=true");
                start.ArgumentList.Add("-p:IncludeNativeLibrariesForSelfExtract=false");
                if (context.Request.SelfContained)
                    start.ArgumentList.Add("-p:EnableCompressionInSingleFile=true");
            }, cancellationToken, publishDirectory).ConfigureAwait(false);

            context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.Publishing,
                "Finalizing Player executable", 0.9f));
            var publishedExecutable = ResolvePublishedExecutable(publishDirectory, context.Target.Platform);
            File.Move(publishedExecutable, executablePath);
            foreach (var symbols in Directory.EnumerateFiles(
                         publishDirectory, "*.pdb", SearchOption.TopDirectoryOnly).ToArray())
            {
                if (context.Request.IncludeDebugSymbols)
                    File.Move(symbols, Path.Combine(
                        layout.AssemblyDirectory, Path.GetFileName(symbols).ToLowerInvariant()));
                else
                    File.Delete(symbols);
            }
            MoveNativeLibraries(publishDirectory, layout.AssemblyDirectory, context.Target.Platform);
            var unexpected = Directory.EnumerateFileSystemEntries(publishDirectory).ToArray();
            if (unexpected.Length != 0)
                throw new InvalidDataException(
                    $"dotnet publish produced unexpected loose Player files: " +
                    string.Join(", ", unexpected.Select(Path.GetFileName)));
        }
        finally
        {
            if (Directory.Exists(publishDirectory)) Directory.Delete(publishDirectory, recursive: true);
        }

        var rootTargetManifest = Path.Combine(context.StagingDirectory, BuildTargetManifest.FileName);
        if (File.Exists(rootTargetManifest))
        {
            var hostTarget = BuildTargetManifestSerializer.Deserialize(File.ReadAllBytes(rootTargetManifest));
            if (!hostTarget.TargetId.Equals(context.Target.TargetId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The platform provider emitted a build-target manifest for a different target.");
            File.Delete(rootTargetManifest);
        }

        var playerResources = BuiltInResourceArchive.Open(layout.PlayerResourceArchivePath);
        var bootstrap = RuntimeMetadataSerializer.Deserialize(playerResources.ReadBytes(
                PlayerPackagedResourceAddresses.RuntimeMetadata)).PlayerBootstrap ??
            throw new InvalidDataException("The packaged runtime metadata has no Player bootstrap section.");
        var packagedTarget = BuildTargetManifestSerializer.Deserialize(playerResources.ReadBytes(
            PlayerPackagedResourceAddresses.BuildTargetManifest));
        if (!bootstrap.ProductName.Equals(workspace.Project.Name, StringComparison.Ordinal) ||
            !bootstrap.Executable.Equals(executableName, StringComparison.Ordinal) ||
            !packagedTarget.TargetId.Equals(context.Target.TargetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The platform provider output does not match the Player bootstrap manifest.");
        ValidatePackagedResourceLayout(layout);
    }

    private static void ValidatePackagedResourceLayout(PlayerBuildLayout layout)
    {
        var expectedDataEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PlayerPackagedResourceAddresses.AssemblyDirectoryName,
            PlayerPackagedResourceAddresses.ResourcesDirectoryName
        };
        var dataEntries = Directory.EnumerateFileSystemEntries(
                layout.DataDirectory, "*", SearchOption.TopDirectoryOnly)
            .ToArray();
        var actualDataEntries = dataEntries
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedDataEntries.SetEquals(actualDataEntries) ||
            dataEntries.Any(static path => !Directory.Exists(path)))
            throw new InvalidDataException(
                "The packaged Player data directory must contain only assembly and resources directories.");

        var expectedResourceEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PlayerPackagedResourceAddresses.PlayerArchiveFileName,
            BuiltInResourceArchive.FileName
        };
        var resourceEntries = Directory.EnumerateFileSystemEntries(
                layout.ResourcesDirectory, "*", SearchOption.TopDirectoryOnly)
            .ToArray();
        var actualResourceEntries = resourceEntries
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedResourceEntries.SetEquals(actualResourceEntries) ||
            resourceEntries.Any(static path => !File.Exists(path)))
            throw new InvalidDataException(
                "The packaged Player resources directory must contain only player.bresources and aot.bresources.");
    }

    private static string ResolvePublishedExecutable(string publishDirectory, BuildTargetPlatform platform)
    {
        var candidates = Directory.EnumerateFiles(publishDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => platform == BuildTargetPlatform.Windows
                ? Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrEmpty(Path.GetExtension(path)))
            .OrderByDescending(path => new FileInfo(path).Length)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidDataException(
                $"dotnet publish did not produce a Player executable in '{publishDirectory}'.");
        return candidates[0];
    }

    private static void MoveNativeLibraries(
        string publishDirectory,
        string assemblyDirectory,
        BuildTargetPlatform platform)
    {
        var libraries = Directory.EnumerateFiles(publishDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => IsNativeLibrary(path, platform))
            .ToArray();
        if (libraries.Length == 0)
            throw new InvalidDataException(
                $"dotnet publish did not produce a native window library for {platform}.");
        foreach (var library in libraries)
            File.Move(library, Path.Combine(assemblyDirectory, Path.GetFileName(library)), overwrite: true);
    }

    private static bool IsNativeLibrary(string path, BuildTargetPlatform platform)
    {
        var name = Path.GetFileName(path);
        return platform switch
        {
            BuildTargetPlatform.Windows => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
            BuildTargetPlatform.Linux => name.Contains(".so", StringComparison.OrdinalIgnoreCase),
            BuildTargetPlatform.MacOS => name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

}
