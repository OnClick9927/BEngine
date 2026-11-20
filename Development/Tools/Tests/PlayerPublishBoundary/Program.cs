using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using BEngine;
using BEngine.Build;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var root = Path.Combine(output, "Run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(root, "Hub"));
DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
var files = FileUtility.Current;
var paths = PathUtility.Current;
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks.Add(name);
}
var project = Path.Combine(root, "Project");
Directory.CreateDirectory(Path.Combine(project, "Assets"));
Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
File.WriteAllText(Path.Combine(project, "Project.yaml"),
    "format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Publication Test\n");
var host = Path.Combine(root, "Host.csproj");
File.WriteAllText(host, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
var target = BuildTargetUtility.Current.Get("windows");
var publisher = new Publisher();
var adapter = new Adapter(publisher, files, paths);
PlayerBuildContext Context(string name, PlayerManagedStrippingLevel level = PlayerManagedStrippingLevel.Full,
    bool selfContained = true) => new(new PlayerBuildRequest
    {
        ProjectPath = project, TargetId = "windows", PlayerHostProjectPath = host,
        ManagedStripping = level, SelfContained = selfContained,
        SplashBackgroundColor = "#142536"
    }, target, Path.Combine(root, name), null);
PlatformBuildContext PlatformContext(PlayerBuildContext context) => new(new PlatformBuildRequest(), target,
    context.StagingDirectory, new("data", "data", "assembly", "resources", "StreamingAssets", "game.exe"));
ProcessRunRequest Configure(PlayerBuildContext context, PlatformBuildHooks hooks)
{
    Directory.CreateDirectory(context.StagingDirectory);
    var process = new ProcessRunRequest("dotnet");
    hooks.ConfigurePublish!(PlatformContext(context), context.StagingDirectory, process);
    return process;
}
var bridge = Context("Bridge");
await adapter.BuildAsync(bridge);
Check(publisher.Last is not null && publisher.Last.Request.ProjectPath == project,
    "The default engine adapter reaches a platform publisher without a content package");
Check(publisher.Last!.Hooks.ConfigurePublish is not null && publisher.Last.Hooks.Published is not null &&
      publisher.Last.Hooks.PrecompiledRegistrationStatement is not null,
    "Default publication and precompiled registration hooks are supplied by Core");
Check(publisher.Last.Hooks.PackagedAssemblySources is null,
    "The default adapter does not infer a content format or discover archives");
var full = Context("Full");
full.StartupLogoPng = new byte[] { 1, 2, 3, 4 };
var fullHooks = adapter.Hooks(full);
var fullProcess = Configure(full, fullHooks);
Check(fullProcess.Arguments.Contains("-p:PublishTrimmed=true") && fullProcess.Arguments.Contains("-p:TrimMode=full"),
    "Full self-contained publication enables the full linker");
Check(fullProcess.Arguments.Contains("-p:SuppressTrimAnalysisWarnings=false") &&
      fullProcess.Arguments.Contains("-p:JsonSerializerIsReflectionEnabledByDefault=true"),
    "Full publication retains reflection serializers and visible linker diagnostics");
var source = Path.Combine(full.StagingDirectory, "BEngineStaticHostBootstrap.cs");
Check(File.Exists(source) && fullProcess.Arguments.Contains("-p:BEngineHostAssemblyBootstrap=" + source),
    "Core writes the bootstrap source and wires it into the SDK invocation");
var sourceText = File.ReadAllText(source);
Check(sourceText.Contains("HostAssemblyRegistry.Register", StringComparison.Ordinal) &&
      sourceText.Contains("AQIDBA==", StringComparison.Ordinal) && sourceText.Contains("#142536", StringComparison.Ordinal),
    "Generated host registration carries the exact startup image and background");
Check(!full.IncludeTextBindings && fullProcess.Arguments.Contains("-p:BEngineIncludeTextBindings=false"),
    "Full publication omits native text bindings when no dynamic input requires them");
var partial = Context("Partial", PlayerManagedStrippingLevel.Conservative);
var partialProcess = Configure(partial, adapter.Hooks(partial));
Check(partialProcess.Arguments.Contains("-p:TrimMode=partial") &&
      partialProcess.Arguments.Contains("-p:SuppressTrimAnalysisWarnings=true") && partial.IncludeTextBindings,
    "Partial publication preserves the previous conservative text and diagnostics policy");
var disabled = Context("Disabled", PlayerManagedStrippingLevel.Disabled);
var disabledProcess = Configure(disabled, adapter.Hooks(disabled));
Check(disabledProcess.Arguments.Contains("-p:PublishTrimmed=false") &&
      !disabledProcess.Arguments.Any(value => value.StartsWith("-p:TrimMode=", StringComparison.Ordinal)),
    "Disabled stripping does not silently choose a linker mode");
var framework = Context("FrameworkDependent", selfContained: false);
var frameworkProcess = Configure(framework, adapter.Hooks(framework));
Check(frameworkProcess.Arguments.Contains("-p:PublishTrimmed=false") && File.Exists(
        Path.Combine(framework.StagingDirectory, "BEngineStaticHostBootstrap.cs")),
    "Framework-dependent publication still generates host registration without forcing trimming");
var quoted = new PlatformPrecompiledAssemblyImage("Game\"\\Code", "sha", "mvid", false);
var register = fullHooks.PrecompiledRegistrationStatement!(quoted);
Check(register == "BEngine.Player.HotUpdate.ManagedCodePrecompiledAssemblies.Register(" +
      JsonSerializer.Serialize(quoted.Name) + ", \"sha\", \"mvid\");",
    "Precompiled registration preserves escaped identities");
Check(fullHooks.PrecompiledRegistrationStatement(quoted with { Shared = true }).Contains(".RegisterShared(", StringComparison.Ordinal),
    "Shared precompiled images retain their separate registration path");

// Exercise dynamic descriptor merging even when SDK/mobile targets force trimming
// while the project itself has disabled stripping.
var dynamic = Context("DynamicDisabled", PlayerManagedStrippingLevel.Disabled);
dynamic.RuntimeManagedCodeAssemblyPaths = [Assembly.GetExecutingAssembly().Location];
dynamic.HostAssetRootDescriptorPath = Path.Combine(root, "host-assets.xml");
File.WriteAllText(dynamic.HostAssetRootDescriptorPath,
    "<linker><assembly fullname=\"Publication.External\"><type fullname=\"Opaque\" preserve=\"all\" /></assembly></linker>");
var dynamicProcess = Configure(dynamic, adapter.Hooks(dynamic));
var descriptor = Path.Combine(dynamic.StagingDirectory, "bengine-dynamic-dependencies.xml");
Check(File.Exists(descriptor) && dynamicProcess.Arguments.Contains("-p:BEngineTrimmerRootDescriptor=" + descriptor),
    "Dynamic contracts are emitted independently of the selected stripping mode");
Check(XDocument.Load(descriptor).Root!.Elements("assembly").Any(element =>
        (string?)element.Attribute("fullname") == "Publication.External"),
    "Host asset roots are merged without requiring the AssetBundles build workflow");
var fullDynamic = Context("DynamicFull");
fullDynamic.RuntimeManagedCodeAssemblyPaths = [Assembly.GetExecutingAssembly().Location];
fullDynamic.StrippingReport = new PlayerStrippingReport();
fullDynamic.StrippingReportPath = Path.Combine(fullDynamic.StagingDirectory, "stripping.yaml");
Configure(fullDynamic, adapter.Hooks(fullDynamic));
Check(File.Exists(Path.ChangeExtension(fullDynamic.StrippingReportPath, ".host-roots.xml")),
    "Full dynamic publication records its precise root descriptor");
Check(!XDocument.Load(Path.Combine(fullDynamic.StagingDirectory, "bengine-dynamic-dependencies.xml"))
        .Root!.Elements("assembly").Any(element => (string?)element.Attribute("fullname") == "PlayerPublishCore"),
    "The host descriptor does not root a dynamic application assembly as a static host");

var packaged = new Dictionary<string, PlatformPackagedAssemblySource>
{
    ["Game.dll"] = new("StreamingAssets/custom.data", 12, "sha256-probe", 2, 6, 10, "image-hash", 0)
};
var callbackCalls = 0;
var contentAdapter = new ContentAdapter(publisher, files, paths, (directory, images) =>
{
    Check(directory == "resources" && images.SequenceEqual(new[] { "Game.dll" }),
        "The content callback receives unchanged platform inputs");
    callbackCalls++;
    return packaged;
});
var contentContext = Context("CustomContent");
var contentHooks = contentAdapter.Hooks(contentContext);
Configure(contentContext, contentHooks);
Check(callbackCalls == 0 && contentHooks.ConfigurePublish is not null && contentHooks.Published is not null,
    "Contributing archive locations does not replace publication or eagerly scan content");
Check(ReferenceEquals(packaged, contentHooks.PackagedAssemblySources!("resources", new[] { "Game.dll" })) && callbackCalls == 1,
    "Exact frozen image locations are passed through without copying or reinterpreting the format");
var failure = new IOException("content input failed");
var throwing = new ContentAdapter(publisher, files, paths, (_, _) => throw failure).Hooks(Context("CallbackFailure"));
try { throwing.PackagedAssemblySources!("resources", []); throw new Exception("Missing callback failure"); }
catch (IOException error) { Check(ReferenceEquals(error, failure), "Content discovery failures are not swallowed by the engine"); }

var failed = Context("PublishFailure");
Directory.CreateDirectory(Path.Combine(failed.StagingDirectory, "bin"));
failed.StrippingReport = new PlayerStrippingReport();
failed.StrippingReportPath = Path.Combine(failed.StagingDirectory, "stripping.yaml");
failed.HostContractPath = "previous-valid-contract";
var ownImage = Assembly.GetExecutingAssembly().Location;
File.Copy(ownImage, Path.Combine(failed.StagingDirectory, "bin", "PlayerPublishCore.dll"));
adapter.Hooks(failed).Published!(PlatformContext(failed), failed.StagingDirectory,
    new ProcessRunResult(1, "publish-output", "publish-error"));
Check(failed.HostContractPath == "previous-valid-contract" && File.Exists(failed.StrippingReportPath),
    "Failed publication records its report without claiming a new host contract");
Check(File.ReadAllText(failed.StrippingReport.HostPublishLogPath) == "publish-outputpublish-error" &&
      failed.StrippingReport.HostAssemblies is [{ Included: false, PublishedBytes: 0 }],
    "Failed SDK output and stripped assembly accounting remain available");
var success = Context("PublishSuccess");
Directory.CreateDirectory(Path.Combine(success.StagingDirectory, "bin"));
Directory.CreateDirectory(Path.Combine(success.StagingDirectory, "linked"));
File.Copy(ownImage, Path.Combine(success.StagingDirectory, "bin", "PlayerPublishCore.dll"));
File.Copy(ownImage, Path.Combine(success.StagingDirectory, "linked", "PlayerPublishCore.dll"));
File.WriteAllBytes(Path.Combine(success.StagingDirectory, "linked", "native.dll"), [1, 2, 3]);
success.StrippingReport = new PlayerStrippingReport();
success.StrippingReportPath = Path.Combine(success.StagingDirectory, "stripping.yaml");
adapter.Hooks(success).Published!(PlatformContext(success), success.StagingDirectory, new ProcessRunResult(0, "ok", ""));
Check(success.HostContractPath is not null && File.Exists(success.HostContractPath) &&
      success.StrippingReport.HostContractPath == success.HostContractPath,
    "Successful publication writes and reports the actual retained host contract");
var contract = JsonSerializer.Deserialize<PlayerHostContract.Document>(File.ReadAllText(success.HostContractPath!))!;
Check(contract.Target == target.TargetId && contract.Assemblies.Keys.SequenceEqual(new[] { "PlayerPublishCore" }),
    "Only valid managed PE images are included in the published contract");
Check(success.StrippingReport.HostAssemblies is [{ Included: true, PublishedBytes: > 0 }],
    "The report measures the actual linked host image");
var fallback = Context("UnlinkedSuccess");
Directory.CreateDirectory(Path.Combine(fallback.StagingDirectory, "bin"));
File.Copy(ownImage, Path.Combine(fallback.StagingDirectory, "bin", "PlayerPublishCore.dll"));
adapter.Hooks(fallback).Published!(PlatformContext(fallback), fallback.StagingDirectory, new ProcessRunResult(0, "", ""));
Check(fallback.HostContractPath is not null && File.Exists(fallback.HostContractPath),
    "Publication without linked output records its untrimmed managed host contract");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is
    "BEngine.AssetBundles" or "BEngine.AssetBundles.Editor" or "BEngine.Graphics" or "BEngine.Graphics.Editor" or "BEngine.IMGUI.Editor"),
    "Default platform publication loads no concrete content, graphics or UI package");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(
    new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PLAYER_PUBLISH_CORE_OK|" + checks.Count);

class Adapter(IPlatformBuildTargetProvider provider, FileUtility files, PathUtility paths)
    : PlayerPlatformBuildTargetAdapter(provider, files, paths)
{
    public PlatformBuildHooks Hooks(PlayerBuildContext context) => CreateBuildHooks(context);
}
sealed class ContentAdapter(IPlatformBuildTargetProvider provider, FileUtility files, PathUtility paths,
    Func<string, IReadOnlyCollection<string>, IReadOnlyDictionary<string, PlatformPackagedAssemblySource>> sources)
    : Adapter(provider, files, paths)
{
    protected override Func<string, IReadOnlyCollection<string>,
        IReadOnlyDictionary<string, PlatformPackagedAssemblySource>> PackagedAssemblySources => sources;
}
sealed class Publisher : IPlatformBuildTargetProvider
{
    public PlatformBuildContext? Last;
    public string ProviderId => "test.publication";
    public bool CanBuild(BuildTargetDescriptor target, out string reason) { reason = ""; return true; }
    public Task BuildAsync(PlatformBuildContext context, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Last = context; return Task.CompletedTask; }
}
