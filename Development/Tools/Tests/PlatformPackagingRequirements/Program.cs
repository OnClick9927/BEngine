using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine.Build;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Platform;
using BEngine.Platform.DotNet;

var output = Path.GetFullPath(args[0]);
var binaries = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks.Add(name);
    Console.WriteLine("PASS|" + name);
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    catch (TargetInvocationException exception) when (exception.InnerException is T) { Check(true, name); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}

Check(ResourcePackagingRequirements.Empty.UncompressedFileExtensions.Count == 0, "No implicit format requirements");
Check(ReferenceEquals(ResourcePackagingRequirements.Empty, ResourcePackagingRequirements.Create([])), "Empty snapshot reuse");
Throws<ArgumentNullException>(() => ResourcePackagingRequirements.Create(null!), "Null collection rejected");
var input = new List<string> { ".Z", ".data", ".DATA", ".archive.bin", ".model-2_0" };
var requirements = ResourcePackagingRequirements.Create(input);
Check(requirements.UncompressedFileExtensions.SequenceEqual(new[] { ".archive.bin", ".data", ".model-2_0", ".z" }),
    "Deterministic case-insensitive normalization and compound extensions");
input.Clear();
Check(requirements.UncompressedFileExtensions.Count == 4, "Input mutation cannot alter snapshot");
var writable = (IList)requirements.UncompressedFileExtensions;
Check(writable.IsReadOnly, "Consumers receive a read-only collection");
Throws<NotSupportedException>(() => writable[0] = ".changed", "Snapshot rejects indexed mutation");
foreach (var invalid in new[] { null, "", ".", "data", "..bin", ".bin.", ".bin..data", ".bad/dir", ".bad\\dir",
             ".a;b", ".a%3Bb", ".a\nb", ".a\rb", ".a\0b", ".a b", ".a\tb", ".a=b", ".a\"b", ".a'b", ".a:b", ".数据", ".a*b", ".a?b" })
    Throws<ArgumentException>(() => ResourcePackagingRequirements.Create([invalid!]),
        "Invalid extension rejected: " + JsonSerializer.Serialize(invalid));
Check(((IPlayerBuildContentProvider)new EmptyContent()).PackagingRequirements == ResourcePackagingRequirements.Empty,
    "Existing content providers retain empty default");

DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
BEngine.DependencyInjection.RuntimeServiceContributions.RegisterDefault<IPlayerStartupImageBuilder>(new TestStartupImages());
var files = FileUtility.Current;
var paths = PathUtility.Current;
var capabilities = new PlatformPlayerBuildCapabilities(files, paths);
var android = BuildTargetUtility.Current.Get("android");
PlatformBuildContext AndroidContext(ResourcePackagingRequirements value, bool appBundle) => new(
    new PlatformBuildRequest { TargetId = android.TargetId, PackagingRequirements = value,
        Options = new PlatformBuildOptions().Set(new AndroidBuildOptions { AndroidBuildAppBundle = appBundle }) }, android, output,
    new PlatformBuildLayout("data", output, output, output, output, output));
var publisher = new DotNetAndroidBuildTargetProvider(ProcessUtility.Current, ApplicationPathUtility.Current,
    files, paths, PlatformUtility.Current, capabilities);
var configure = typeof(DotNetAndroidBuildTargetProvider).GetMethod("ConfigurePublish", BindingFlags.Instance | BindingFlags.NonPublic)!;
ProcessRunRequest PublishArguments(ResourcePackagingRequirements value, bool appBundle = true)
{
    var process = new ProcessRunRequest("dotnet");
    configure.Invoke(publisher, [process, AndroidContext(value, appBundle)]);
    return process;
}
var empty = PublishArguments(ResourcePackagingRequirements.Empty);
Check(!empty.Arguments.Any(argument => argument.Contains("Uncompressed", StringComparison.Ordinal)), "Empty provider leaves Android defaults intact");
Check(empty.Arguments.Contains("-p:AndroidPackageFormat=aab"), "Default Android app bundle option retained");
var processArguments = PublishArguments(requirements, appBundle: false);
var compressionArgument = processArguments.Arguments.Single(argument => argument.StartsWith("-p:BEngineUncompressedResourceExtensions=", StringComparison.Ordinal));
Check(compressionArgument == "-p:BEngineUncompressedResourceExtensions=.archive.bin%3B.data%3B.model-2_0%3B.z",
    "Android transports all provider requirements as one escaped MSBuild property");
Check(processArguments.Arguments.Contains("-p:AndroidPackageFormat=apk"), "Existing Android package options retained");
File.WriteAllText(Path.Combine(output, "AndroidProperty.txt"), compressionArgument);
var propertyHelper = typeof(DotNetPlatformBuildTargetProvider).Assembly.GetType("BEngine.Editor.DotNetBuildHostLocator", true)!
    .GetMethod("AddAdditionalProperties", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (var property in new[] { "BEngineUncompressedResourceExtensions", "bengineuncompressedresourceextensions", "AndroidStoreUncompressedFileExtensions" })
    Throws<ArgumentException>(() => propertyHelper.Invoke(null, [new ProcessRunRequest("dotnet"), new PlatformBuildRequest
    { AdditionalMsBuildProperties = new Dictionary<string, string> { [property] = ".override" } }]),
        "Untyped properties cannot override format requirements: " + property);

var project = Path.Combine(output, "project");
Directory.CreateDirectory(project);
File.WriteAllText(Path.Combine(project, "Project.yaml"), "format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Packaging Test\n");
Directory.CreateDirectory(Path.Combine(project, "Assets", "Scenes"));
// This provider owns its data format; the generic pipeline only validates the configured scene address.
File.WriteAllText(Path.Combine(project, "Assets", "Scenes", "Main.scene.yaml"), "{}\n");
var content = new CustomContent(requirements);
var pipeline = new PlayerBuildPipelineAdapter(ProcessUtility.Current, ApplicationPathUtility.Current, files, paths,
    PlatformUtility.Current, capabilities, content);
Check(capabilities.TryGetHostProject(BuildTargetUtility.Current.Get("windows").TargetId, out var sourceHost) &&
    sourceHost.Contains(Path.Combine("Platform", "Player", "Hosts"), StringComparison.Ordinal),
    "Source host discovery follows the platform Player path");
foreach (var provider in pipeline.RegisteredProviders) pipeline.UnregisterProvider(provider.ProviderId);
var capture = new CapturePlatform();
pipeline.RegisterProvider(new PlayerPlatformBuildTargetAdapter(capture, files, paths));
var result = await pipeline.BuildAsync(new PlayerBuildRequest
{
    ProjectPath = project, OutputDirectory = Path.Combine(output, "published"), TargetId = "windows",
    ReplaceExisting = true, CompileShaders = false,
    PlayerHostProjectPath = Path.Combine(binaries, "BuildHosts", "Desktop", "BEngine.Player.DesktopHost.csproj")
});
Check(content.Reads == 1, "Build snapshots content requirements once");
Check(ReferenceEquals(requirements, content.Prepared!.PackagingRequirements), "Content preparation observes declared snapshot");
Check(ReferenceEquals(requirements, capture.Context!.Request.PackagingRequirements), "Core pipeline and platform adapter preserve content requirements");
Check(File.Exists(Path.Combine(result.OutputDirectory, "prepared.txt")), "Generic content build publishes successfully without a format-specific provider");
Check(capture.Context.Request.EngineRuntimeRoot is not null, "Exported host resolution still locates the runtime");

// Repeated platform requests share the immutable value; no per-frame or per-access copying.
var request = new PlatformBuildRequest { PackagingRequirements = requirements };
_ = ReadRequirements(request); _ = ReadRequirements(request);
var (total, allocated) = ReadRequirements(request);
Check(total == 400000 && allocated == 0, "100000 warmed requirement reads allocate zero bytes");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, AllocatedBytes = allocated, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));

[MethodImpl(MethodImplOptions.NoInlining)]
static (int Total, long AllocatedBytes) ReadRequirements(PlatformBuildRequest request)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var total = 0;
    for (var index = 0; index < 100000; index++) total += request.PackagingRequirements.UncompressedFileExtensions.Count;
    return (total, GC.GetAllocatedBytesForCurrentThread() - allocated);
}

sealed class EmptyContent : IPlayerBuildContentProvider
{
    public Task PrepareAsync(PlayerBuildContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}
sealed class CustomContent(ResourcePackagingRequirements requirements) : IPlayerBuildContentProvider
{
    public int Reads;
    public PlayerBuildContext? Prepared;
    public ResourcePackagingRequirements PackagingRequirements { get { Reads++; return requirements; } }
    public Task PrepareAsync(PlayerBuildContext context, CancellationToken cancellationToken)
    {
        Prepared = context;
        File.WriteAllText(Path.Combine(context.StagingDirectory, "prepared.txt"), "custom content");
        return Task.CompletedTask;
    }
}
sealed class CapturePlatform : IPlatformBuildTargetProvider
{
    public PlatformBuildContext? Context;
    public string ProviderId => "test.capture";
    public bool CanBuild(BuildTargetDescriptor target, out string reason) { reason = ""; return target.Platform == BuildTargetPlatform.Windows; }
    public Task BuildAsync(PlatformBuildContext context, CancellationToken cancellationToken = default)
    { Context = context; return Task.CompletedTask; }
}
sealed class TestStartupImages : IPlayerStartupImageBuilder
{
    public PlayerStartupImages Build(BEngine.ProjectSystem.ProjectWorkspace workspace, PlayerBuildRequest request) =>
        new(new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 });
}
