using System.Text.Json;
using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor;

public sealed class DotNetWebAssemblyBuildTargetProvider : DotNetPlatformBuildTargetProvider
{
    public DotNetWebAssemblyBuildTargetProvider()
    {
    }

    public DotNetWebAssemblyBuildTargetProvider(
        ProcessUtility processRunner,
        ApplicationPathUtility applicationPaths,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities) : base(
        processRunner, applicationPaths, fileSystem, pathService, platformService,
        buildCapabilities)
    {
    }

    public override string ProviderId => "bengine.dotnet-webassembly";
    protected override BuildTargetPlatform Platform => BuildTargetPlatform.Web;

    protected override IEnumerable<PlayerBuildPrerequisite> GetPlatformPrerequisites(
        BuildTargetDescriptor target,
        DotNetBuildEnvironment environment)
    {
        yield return Workload(environment, "wasm-workload",
            ["wasm-tools", "wasm-experimental"],
            "The .NET WebAssembly workload is installed; Release builds AOT the Host/Core and can take substantially longer.",
            "The .NET WebAssembly workload is not installed.",
            "Run 'dotnet workload install wasm-tools'.");
    }

    protected override void ConfigurePublish(ProcessRunRequest process, PlayerBuildContext context)
    {
        RequireSelfContained(context);
        // Make the platform a global restore property so the BEngine.Player project graph and
        // its platform-suffixed artifacts directory agree during publish.
        process.Arguments.Add("-p:BEnginePlayerPlatform=Web");
        process.Arguments.Add("-p:PublishTrimmed=true");
        process.Arguments.Add(context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full
            ? "-p:TrimMode=full" : "-p:TrimMode=partial");
        process.Arguments.Add(context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full
            ? "-p:SuppressTrimAnalysisWarnings=false" : "-p:SuppressTrimAnalysisWarnings=true");
        process.Arguments.Add("-p:RunAOTCompilation=true");
        process.Arguments.Add("-p:WasmBuildNative=true");
        process.Arguments.Add("-p:WasmDedup=false");
        process.Arguments.Add("-p:WasmNativeDebugSymbols=false");
    }

    protected override void ValidateOutput(PlayerBuildContext context)
    {
        var webRoot = PathService.Combine(context.StagingDirectory, "wwwroot");
        StagePlayerContent(context, webRoot);
        if (!FileSystem.FileExists(PathService.Combine(webRoot, "index.html")) ||
            !FileSystem.FileExists(PathService.Combine(webRoot, "main.js")) ||
            !FileSystem.FileExists(PathService.Combine(webRoot, "bengine-content.json")) ||
            !FileSystem.FileExists(PathService.Combine(
                webRoot, "_framework", "dotnet.js")))
            throw new InvalidDataException(
                "The WebAssembly Player Host did not produce a deployable wwwroot with " +
                "index.html, main.js, bengine-content.json, and _framework/dotnet.js.");
    }

    private void StagePlayerContent(PlayerBuildContext context, string webRoot)
    {
        var dataDirectories = FileSystem.EnumerateDirectories(
                context.StagingDirectory, "*_data", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (dataDirectories.Length != 1)
            throw new InvalidDataException(
                $"The Web Player requires exactly one staged '*_data' directory; found " +
                $"{dataDirectories.Length} in '{context.StagingDirectory}'.");
        var dataDirectory = dataDirectories[0];
        var dataDirectoryName = PathService.GetFileName(dataDirectory);

        var contentRoot = PathService.Combine(webRoot, "bengine-content");
        var entries = new List<WebContentEntry>();
        foreach (var source in FileSystem.EnumerateFiles(
                     dataDirectory, "*", SearchOption.AllDirectories)
                 .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relativePath = PathService.GetRelativePath(dataDirectory, source)
                .Replace('\\', '/');
            if (relativePath.StartsWith("../", StringComparison.Ordinal) ||
                relativePath.Equals("..", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Web Player content escaped its data directory: '{source}'.");

            var destination = PathService.Combine(
                contentRoot, relativePath.Replace('/', PathService.DirectorySeparatorChar));
            FileSystem.CreateDirectory(PathService.GetDirectoryName(destination)!);
            FileSystem.CopyFile(source, destination, overwrite: true);
            var encodedPath = string.Join('/', relativePath.Split('/')
                .Select(Uri.EscapeDataString));
            entries.Add(new WebContentEntry(
                $"../bengine-content/{encodedPath}",
                $"{dataDirectoryName}/{relativePath}"));
        }

        if (entries.Count == 0)
            throw new InvalidDataException("The Web Player has no packaged content to mount.");

        const string assemblyDirectoryMarker = "assembly/.bengine-directory";
        var markerPath = PathService.Combine(
            contentRoot, assemblyDirectoryMarker.Replace('/', PathService.DirectorySeparatorChar));
        FileSystem.CreateDirectory(PathService.GetDirectoryName(markerPath)!);
        FileSystem.WriteAllBytes(markerPath, []);
        entries.Add(new WebContentEntry(
            $"../bengine-content/{assemblyDirectoryMarker}",
            $"{dataDirectoryName}/{assemblyDirectoryMarker}"));

        var manifest = JsonSerializer.Serialize(
            new WebContentManifest(dataDirectoryName, entries),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        FileSystem.WriteAllText(PathService.Combine(webRoot, "bengine-content.json"), manifest);
    }

    private sealed record WebContentManifest(
        string DataDirectory,
        IReadOnlyList<WebContentEntry> Files);

    private sealed record WebContentEntry(string Url, string VirtualPath);
}
