using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Instrumentation;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.Profiling;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new InvalidOperationException(name); }
T Complete<T>(BTask<T> operation)
{
    var timeout = Stopwatch.StartNew();
    while (!operation.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
    { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(1)); Thread.Yield(); }
    if (!operation.IsCompleted) throw new TimeoutException("Owner-thread operation did not finish.");
    return operation.GetAwaiter().GetResult();
}
DotNetPlatformServices.Initialize();
AssetPipelineRuntime.Initialize();
Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName == typeof(ProbeAsset).Assembly.GetName().Name),
    "Contract consumer has no Core private-access grant");
Check(!typeof(SceneLoadData).GetFields(BindingFlags.Public | BindingFlags.Instance).Any(), "Scene input exposes no mutable document fields");
Throws<ArgumentException>(() => SceneAssetLoading.Restore(default), "Default scene input is rejected before object creation");
Throws<ArgumentNullException>(() => RuntimeAssetDecoding.HasDecoder(null!), "Null decoder lookup is rejected");
Throws<ArgumentNullException>(() => RuntimeAssetDecoding.DecodeText((byte[])null!), "Null byte-buffer text semantics are preserved");
foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32, Encoding.GetEncoding(12001) })
{
    var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("中文Ω")).ToArray();
    Check(RuntimeAssetDecoding.DecodeText(bytes) == "中文Ω", "BOM text decoding preserves " + encoding.WebName);
}
Check(RuntimeAssetDecoding.DecodeText(new byte[] { 0xE2 }) == "�", "Truncated UTF-8 retains replacement behavior");
var text = (TextAsset)RuntimeAssetDecoding.Decode("plain"u8.ToArray(), "virtual://message.txt", typeof(TextAsset))!;
Check(text.text == "plain" && text.path == "virtual://message.txt", "Fundamental text decoding is independent of the storage package");
var id = Guid.NewGuid();
var ownerId = Guid.NewGuid();
var metadata = new RuntimeResourceMetadata("Assets/message.txt", "virtual://", "Assets/message.txt", id, id, 0,
    "TextAsset", "", new Dictionary<string, string>());
RuntimeAssetReferences.Bind(text, metadata);
Check(text.Id == id && text.guid == id.ToString("N") && text.assetPath == metadata.Address && text.sourcePath == "" && text.artifactPath == "",
    "Storage binding preserves logical identity without inventing physical paths");
var originalId = text.Id;
Throws<ArgumentException>(() => RuntimeAssetReferences.Bind(text, metadata with { Guid = Guid.Empty }), "Malformed identity is rejected");
Check(text.Id == originalId && text.assetPath == metadata.Address, "Rejected identity does not partially mutate the asset");
RuntimeAssetReferences.Bind(text, metadata with { Guid = id, OwnerGuid = ownerId, LocalIdentifier = 99, OwnerAddress = "Assets/owner" });
Check(text.Id == id && text.guid == ownerId.ToString("N") && text.assetPath == "Assets/owner", "Sub-assets retain object identity and owner reference");
RuntimeAssetReferences.ValidateOptionalRelativePath("Assets/sub/file.bin", "path");
RuntimeAssetReferences.ValidateOptionalRelativePath("", "path");
Check(true, "Relative and empty optional paths remain accepted");
Throws<InvalidDataException>(() => RuntimeAssetReferences.ValidateOptionalRelativePath("../escape", "path"), "Parent traversal is rejected");
Throws<InvalidDataException>(() => RuntimeAssetReferences.ValidateOptionalRelativePath("C:/escape", "path"), "Rooted paths are rejected");
BObject.DestroyImmediate(text);
Throws<ObjectDisposedException>(() => RuntimeAssetReferences.Bind(text, metadata), "Destroyed assets cannot acquire new storage identity");

RuntimeAssetCodecRegistry.Register<ProbeAsset>((bytes, path) => new ProbeAsset { Value = Encoding.UTF8.GetString(bytes.Span), name = path });
Check(RuntimeAssetDecoding.HasDecoder(typeof(ProbeAsset)), "A provider can discover an externally registered decoder");
Check(RuntimeAssetDecoding.TryDecodeRegistered("override"u8.ToArray(), "probe", typeof(ProbeAsset), out var custom) && ((ProbeAsset)custom).Value == "override",
    "Custom byte decoding retains precedence");
var asynchronous = Complete(RuntimeAssetDecoding.DecodeRegisteredAsync("async"u8.ToArray(), "probe", typeof(ProbeAsset), new()));
Check(asynchronous is ProbeAsset { Value: "async" }, "Asynchronous providers use the same registered decoder");
BObject.DestroyImmediate(custom); BObject.DestroyImmediate(asynchronous!);
RuntimeAssetCodecRegistry.Unregister<ProbeAsset>();
Check(!RuntimeAssetDecoding.HasDecoder(typeof(ProbeAsset)), "Removing a decoder is immediately visible to providers");

var sceneId = Guid.NewGuid(); var rootId = Guid.NewGuid(); var transformId = Guid.NewGuid();
var yaml = $$"""
    format: BEngine.Scene
    version: 2
    id: {{sceneId}}
    name: Contract scene
    gameObjects:
    - id: {{rootId}}
      name: Preserved root
      active: true
      transform:
        id: {{transformId}}
        localPosition: { x: '12.5', y: '-3.25' }
    prefabInstances: []
    """;
var source = Encoding.UTF8.GetBytes(yaml);
foreach (var bytes in new[] { source, BinaryDataSerializer.CompileYaml(source) })
{
    var data = SceneAssetLoading.ReadData(bytes);
    using var scene = SceneAssetLoading.Restore(data);
    Check(scene.Id == sceneId && scene.gameObjects.Single().Id == rootId && scene.gameObjects.Single().transform.Id == transformId,
        "YAML/compiled scene preserves scene, object and Transform GUIDs");
    Check(scene.name == "Contract scene" && scene.gameObjects.Single().transform.localPosition.x == Fix64.Parse("12.5"),
        "Scene hydration preserves names and fixed-point state");
    RuntimeAssetReferences.BindScenePath(scene, "Assets/Contract.scene.yaml");
    Check(scene.path == "Assets/Contract.scene.yaml", "Scene path is bound through the storage contract");
    var progress = new List<float>();
    using var restored = Complete(SceneAssetLoading.RestoreAsync(data, null, new(default, progress.Add)));
    Check(restored.Id == sceneId && progress.Count > 1 && progress[^1] == 1 && progress.Zip(progress.Skip(1)).All(p => p.First <= p.Second),
        "Asynchronous scene restoration keeps detailed monotonic progress to completion");
    using var cancellation = new BCancellationSource(); cancellation.Cancel();
    var count = RuntimeObjectDiagnostics.CountCurrentDomainObjects();
    Throws<BOperationCanceledException>(() => Complete(SceneAssetLoading.RestoreAsync(data, null, new(cancellation.Token))), "Canceled scene hydration does not run");
    Check(RuntimeObjectDiagnostics.CountCurrentDomainObjects() == count, "Canceled scene hydration leaves no engine objects");
}
var sourceFile = Path.Combine(output, "Contract.scene.yaml"); File.WriteAllText(sourceFile, yaml);
using (var fromSource = SceneAssetLoading.LoadSource(sourceFile))
    Check(fromSource.Id == sceneId && fromSource.gameObjects.Count == 1, "Optional source document codec stays behind the same scene-loading boundary");
var managed = $"format: BEngine.ManagedAsset\nversion: 1\ntypeName: {typeof(ManagedProbeAsset).AssemblyQualifiedName}\ndata: |\n  value: 17\n";
var decodedManaged = (ManagedProbeAsset)RuntimeAssetDecoding.Decode(Encoding.UTF8.GetBytes(managed), "virtual://custom.asset.yaml", typeof(ManagedProbeAsset))!;
Check(decodedManaged.Value == 17, "Legacy managed asset envelope is decoded by Core without AB format branches");
BObject.DestroyImmediate(decodedManaged);

using var services = new ServiceCollection().AddBEngineDotNetPlatform().BuildServiceProvider();
var settings = new RuntimeSettingsData { CompanyName = "ContractCompany", ProductName = "ContractProduct" };
var workspace = RuntimeWorkspace.CreateRuntime(output, new RuntimeProjectData(), settings,
    services.GetRequiredService<FileUtility>(), services.GetRequiredService<PathUtility>());
RuntimeConfiguration.Apply(workspace);
Check(Application.companyName == settings.CompanyName && Application.productName == settings.ProductName,
    "Core applies application identity with the rest of runtime settings");
settings.CompanyName = "";
Throws<InvalidDataException>(() => RuntimeConfiguration.Apply(workspace), "Invalid runtime identity is rejected");
Check(Application.companyName == "ContractCompany", "Rejected settings do not partially overwrite application identity");
var dataDirectory = Path.Combine(output, "DataNotCreated");
PlayerHostServices.ConfigureDataDirectory(dataDirectory);
Check(Application.dataPath == Path.GetFullPath(dataDirectory) && !Directory.Exists(dataDirectory), "Host data-path configuration performs no filesystem writes");
Throws<ArgumentException>(() => PlayerHostServices.ConfigureDataDirectory(" "), "Blank host data directory is rejected");
Check(Application.dataPath == Path.GetFullPath(dataDirectory), "Rejected data directory leaves the current environment intact");

var observed = 0; var sample = new ProfilerSample(1, 0, "Contract", "Sample", 1, 0, 1, "owner", []);
void Observe(ProfilerSample value) { if (value.token == sample.token) observed++; }
Profiler.sampleCompleted += Observe;
var first = Profiler.RegisterRecorder(new Recorder());
var second = Profiler.RegisterRecorder(new Recorder());
first.PublishSample(sample); second.PublishSample(sample);
Check(observed == 2, "Each live recorder registration can publish its own completed samples");
first.Dispose(); first.PublishSample(sample); second.PublishSample(sample);
Check(observed == 3, "Disposing an older recorder rejects its publications and preserves the newer recorder");
second.Dispose(); second.Dispose(); second.PublishSample(sample);
Check(observed == 3, "Recorder disposal is idempotent and stops subsequent publication");
using (var recorder = Profiler.RegisterRecorder(new Recorder()))
{
    var warmupBytes = MeasurePublications(recorder, sample);
    var publicationBytes = MeasurePublications(recorder, sample);
    File.WriteAllText(Path.Combine(output, "PublicationDiagnostics.json"), JsonSerializer.Serialize(new
    {
        AllocatedBytes = publicationBytes,
        WarmupAllocatedBytes = warmupBytes,
        Publications = 10000
    }));
    Check(publicationBytes == 0, "Registered sample publication allocates zero bytes in steady state");
}
Profiler.sampleCompleted -= Observe;
var liveBefore = RuntimeObjectDiagnostics.CountCurrentDomainObjects();
var probe = new ProbeAsset();
Check(!RuntimeObjectDiagnostics.IsRuntimeOnly(probe) && RuntimeObjectDiagnostics.CountCurrentDomainObjects() == liveBefore + 1,
    "Read-only object diagnostics retain visible object-count semantics");
BObject.DestroyImmediate(probe);
Check(RuntimeObjectDiagnostics.CountCurrentDomainObjects() == liveBefore, "Destroyed objects are omitted from registry observations");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor"),
    "Resource and scene contracts require no graphics, window or UI package");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("ASSET_STORAGE_CONTRACT_OK|" + checks.Count);

[MethodImpl(MethodImplOptions.NoInlining)]
static long MeasurePublications(Profiler.RecorderRegistration recorder, ProfilerSample sample)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++) recorder.PublishSample(sample);
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

public sealed class ProbeAsset : BAsset { public string Value = ""; }
public sealed class ManagedProbeAsset : BAsset { public int Value; }
sealed class Recorder : IProfilerRecorder
{
    public long BeginSample(string category, string name, bool captureCallstacks) => 1;
    public void EndSample() { }
    public void EndSample(long token) { }
}
