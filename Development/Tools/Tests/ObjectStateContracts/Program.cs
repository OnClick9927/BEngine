using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;
using BEngine.Serialization;
using BEngine.Video;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

// Reuse this test apphost as a deterministic ffprobe feed. No installed FFmpeg,
// network, video fixture or developer preferences are involved.
if (args.Contains("-show_entries"))
{
    if (args.Contains("frame=best_effort_timestamp_time"))
        Console.Write("0.000\n0.033,side data\n,empty first field\ninvalid\nNaN\nInfinity\n1e-1,side data\n 0.2 \n0.033,duplicate\n");
    else
        Console.Write("{\"streams\":[{\"codec_type\":\"video\",\"width\":2,\"height\":1,\"avg_frame_rate\":\"30/1\"}],\"format\":{\"duration\":\"1.0\"}}");
    return;
}

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var legacy = Path.GetFullPath(args[1]);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
DotNetPlatformServices.Initialize();
RuntimeHelpers.RunModuleConstructor(typeof(Document<AnimationClip>).Module.ModuleHandle);
RuntimeHelpers.RunModuleConstructor(typeof(AnimationClip).Module.ModuleHandle);
foreach (var assembly in new[] { typeof(BObject).Assembly, typeof(AnimationClip).Assembly, typeof(TextureAtlas).Assembly })
    Check(!assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a =>
        a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name), assembly.GetName().Name + " grants no access to the consumer");
foreach (var name in new[] { "ComponentGraphData", "SerializedMemberData", "SerializedValueData" })
    Check(!typeof(SerializationDataAdapter).Assembly.GetType("BEngine.Serialization." + name)!.IsPublic,
        name + " stays internal in the serialization assembly");
Check(typeof(ObjectStateSnapshot).GetConstructors().Length == 0, "Object-state snapshots cannot be constructed in an uninitialized state");

var node = new GraphNode { Value = 17 }; node.Next = node;
var source = new GraphAsset { First = node, Second = node, Numbers = [1, 2, 3], Count = 11 };
var captured = ObjectStateSnapshot.Capture(source);
source.First.Value = 91; source.Numbers.Add(4);
var target = new GraphAsset(); var identity = target.Id;
captured.Restore(target);
Check(target.Id == identity && target.Count == 11, "Restoring state preserves the target object identity");
Check(target.First.Value == 17 && target.Numbers.SequenceEqual([1, 2, 3]), "Capture detaches managed state from subsequent source edits");
Check(ReferenceEquals(target.First, target.Second) && ReferenceEquals(target.First, target.First.Next), "Shared and cyclic managed references survive restoration");
target.First.Value = 42;
var second = new GraphAsset(); captured.Clone().Restore(second);
Check(second.First.Value == 17 && !ReferenceEquals(second.First, target.First), "Snapshots remain reusable and clones restore independent graphs");
var selected = ObjectStateSnapshot.Capture(source, [nameof(GraphAsset.Count)]);
var subset = new GraphAsset { Count = -1 }; selected.Restore(subset);
Check(selected.MemberCount == 1 && subset.Count == 11 && subset.Numbers.Count == 0, "Subset capture restores only selected persistent members");
Throws<ArgumentException>(() => ObjectStateSnapshot.Capture(source, [nameof(BObject.Id)]), "Base identity is not a selectable persistent member");
Throws<ArgumentException>(() => ObjectStateSnapshot.Capture(source, ["missing"]), "Unknown capture members are rejected");
Throws<ArgumentNullException>(() => ObjectStateSnapshot.Capture(null!), "Null capture source is rejected");
Throws<ArgumentNullException>(() => captured.Restore(null!), "Null restoration target is rejected");
BObject.DestroyImmediate(source);
Throws<ObjectDisposedException>(() => ObjectStateSnapshot.Capture(source), "Destroyed sources are rejected");
BObject.DestroyImmediate(target);
Throws<ObjectDisposedException>(() => captured.Restore(target), "Destroyed targets are rejected");
var yaml = captured.ToYaml();
Check(yaml.StartsWith("version: 1\n", StringComparison.Ordinal) && !yaml.Contains("_data") && !yaml.Contains("memberCount"), "Opaque state retains the existing graph mapping without wrapper fields");
var parsed = ObjectStateSnapshot.Parse(yaml); parsed.Restore(second);
Check(parsed.Version == 1 && second.First.Value == 17 && ReferenceEquals(second.First, second.Second), "YAML restores the opaque graph snapshot");
Throws<InvalidDataException>(() => ObjectStateSnapshot.Parse(yaml.Replace("version: 1", "version: 9")), "Unsupported graph versions fail explicitly");
var bytes = BinaryDataSerializer.CompileYaml(Encoding.UTF8.GetBytes(yaml));
BinaryDataSerializer.Deserialize<ObjectStateSnapshot>(bytes).Restore(second);
Check(second.First.Value == 17 && ReferenceEquals(second.First.Next, second.First), "Compiled data binds the same opaque graph format");
var nested = YamlUtility.Serialize(new SnapshotEnvelope { State = captured });
YamlUtility.Deserialize<SnapshotEnvelope>(nested).State!.Restore(second);
Check(second.Numbers.Count == 3, "Snapshots serialize as nested document members");
var nullable = BinaryDataSerializer.CompileYaml("state: null\n"u8);
Check(BinaryDataSerializer.Deserialize<SnapshotEnvelope>(nullable).State is null, "Compiled nullable snapshots retain null semantics");

var link = new GameObject("Referenced object");
var component = new GameObject("Source").AddComponent<ReferenceComponent>(); component.Link = link;
var other = new GameObject("Target").AddComponent<ReferenceComponent>();
ObjectStateSnapshot.Capture(component).Restore(other, id => id == link.Id ? link : null);
Check(ReferenceEquals(other.Link, link), "Component snapshots use the caller's scene-object resolver");

var boxText = YamlUtility.Serialize(new ValueBox(7));
Check(boxText.Trim() == "value: 7", "A consumer-owned adapter controls its existing wire schema");
Check(YamlUtility.Deserialize<ValueBox>(boxText).Value == 7, "Consumer-owned adapters restore through the text provider");
Check(BinaryDataSerializer.Deserialize<ValueBox>(BinaryDataSerializer.CompileYaml(Encoding.UTF8.GetBytes(boxText))).Value == 7,
    "Consumer-owned adapters restore through the independent binary binder");
Throws<InvalidOperationException>(() => SerializationDataAdapter.Find(typeof(BadBox)), "Mismatched adapter identities are rejected");
Throws<InvalidOperationException>(() => SerializationDataAdapter.Find(typeof(CycleBox)), "Adapter chains cannot recurse without consuming document data");
var deepYaml = new StringBuilder();
for (var i = 0; i < 63; i++) deepYaml.Append(' ', i * 2).Append("next:\n");
deepYaml.Append(' ', 126).Append("value: 19\n");
var deepBytes = BinaryDataSerializer.CompileYaml(Encoding.UTF8.GetBytes(deepYaml.ToString()));
var deep = BinaryDataSerializer.Deserialize<DepthBox>(deepBytes).Data;
for (var i = 0; i < 63; i++) deep = deep.Next!;
Check(deep.Value == 19, "Adapters preserve the existing maximum wire-depth allowance");
_ = MeasureLookups(); _ = MeasureLookups();
var lookupBytes = MeasureLookups();
Console.WriteLine($"WARM_ADAPTER_LOOKUP_BYTES|{lookupBytes}");
Check(lookupBytes == 0, "Warm adapter lookups allocate no memory");

var clipBytes = File.ReadAllBytes(Path.Combine(legacy, "clip.anim.yaml"));
Check(RuntimeAssetDecoding.TryDecodeRegistered(clipBytes, "legacy.anim.yaml", typeof(AnimationClip), out var clipAsset), "The package decoder accepts previous-version clip bytes");
var clip = (AnimationClip)clipAsset;
Check(clip.bindings.Count == 1 && clip.bindings[0].curve.Evaluate(0) == (Fix64)12 && clip.frameRate == (Fix64)30 && clip.events[0].functionName == "OnFootstep", "Legacy curves, events and clip properties remain intact");
var controllerBytes = File.ReadAllBytes(Path.Combine(legacy, "controller.controller.yaml"));
Check(RuntimeAssetDecoding.TryDecodeRegistered(controllerBytes, "legacy.controller.yaml", typeof(AnimatorController), out var controllerAsset), "The package decoder accepts previous-version controller bytes");
var controller = (AnimatorController)controllerAsset;
Check(controller.states.Count == 2 && controller.states[0].transitions[0].destinationState == "Run" &&
    controller.layers.Count == 1 && controller.layers[0].defaultState == "OverlayIdle", "Legacy controller states, transitions and layers remain intact");

RuntimeHelpers.RunModuleConstructor(typeof(TextureAtlas).Module.ModuleHandle);
var atlasText = File.ReadAllText(Path.Combine(legacy, "atlas.yaml"));
var atlas = YamlUtility.Deserialize<TextureAtlas>(atlasText);
Check(atlas.Id == Guid.Parse("dab153ae-d11d-440e-9232-8ad789d8cb54") && atlas.Padding == 3, "Atlas hydration preserves a previous-version persistent GUID");
var atlasModule = typeof(TextureAtlas).Assembly;
var registration = atlasModule.GetType("BEngine.TextureAtlasRegistration")!;
var converter = registration.GetField("Converter", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
Check(YamlUtility.TryGetExclusiveConverterVersion(converter, out var generation), "Atlas recognizes its exclusive converter generation");
var compiledAtlas = File.ReadAllBytes(Path.GetFullPath(args[2]));
Check(compiledAtlas.AsSpan().StartsWith("BTAR"u8), "Atlas compilation retains the validated compact envelope");
Check(RuntimeAssetDecoding.TryDecodeRegistered(compiledAtlas, "atlas.yaml", typeof(TextureAtlas), out var decodedAtlas) && decodedAtlas.Id == atlas.Id, "Compact atlas decoding preserves identity");
YamlUtility.RegisterConverter(new ForeignConverter());
Check(!YamlUtility.TryGetExclusiveConverterVersion(converter, out var changedGeneration) && changedGeneration != generation,
    "An additional converter invalidates the exclusive generation");
Check(RuntimeAssetDecoding.TryDecodeRegistered(compiledAtlas, "atlas.yaml", typeof(TextureAtlas), out var replay) && replay.Id == atlas.Id,
    "A compiled atlas replays its preserved source after converter changes");
var corrupt = compiledAtlas.ToArray(); corrupt[16] ^= 0x7F;
Throws<InvalidDataException>(() => RuntimeAssetDecoding.TryDecodeRegistered(corrupt, "atlas.yaml", typeof(TextureAtlas), out _),
    "A foreign converter cannot hide corrupt compiled atlas data");
YamlUtility.UnregisterConverters(Assembly.GetExecutingAssembly());
Check(YamlUtility.TryGetExclusiveConverterVersion(converter, out var restoredGeneration) && restoredGeneration != changedGeneration,
    "Converter removal publishes a fresh compatible generation");

var apphost = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".exe");
var probeHost = Path.Combine(Path.GetDirectoryName(apphost)!, "ffprobe.exe");
File.Copy(apphost, probeHost, overwrite: true);
var previousFfmpeg = Environment.GetEnvironmentVariable("BENGINE_FFMPEG_PATH"); var previousCulture = CultureInfo.CurrentCulture;
try
{
    Environment.SetEnvironmentVariable("BENGINE_FFMPEG_PATH", apphost); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    using var reader = new FfmpegVideoFrameReader("deterministic-fixture.webm");
    Check(reader.indexedFrameCount == 4 && reader.frameRate == 30 && reader.duration == 1,
        "Video indexing handles whole lines, empty first CSV fields, exponents, duplicates and invariant culture");
}
finally { Environment.SetEnvironmentVariable("BENGINE_FFMPEG_PATH", previousFfmpeg); CultureInfo.CurrentCulture = previousCulture; }
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is { } name &&
    (name == "BEngine.Core.Editor" || name.EndsWith(".Editor", StringComparison.Ordinal))),
    "Runtime object-state checks consume Editor-built atlas bytes without loading Editor");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"OBJECT_STATE_CONTRACTS_OK|checks={checks.Count}");

[MethodImpl(MethodImplOptions.NoInlining)]
static long MeasureLookups()
{
    var start = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++) _ = SerializationDataAdapter.Find(typeof(ObjectStateSnapshot));
    return GC.GetAllocatedBytesForCurrentThread() - start;
}

public sealed class GraphAsset : ScriptableObject
{
    public GraphNode First { get; set; } = new(); public GraphNode Second { get; set; } = new();
    public List<int> Numbers { get; set; } = []; public int Count { get; set; }
}
public sealed class GraphNode { public int Value { get; set; } public GraphNode? Next { get; set; } }
public sealed class ReferenceComponent : MonoBehaviour { public GameObject? Link { get; set; } }
public sealed class SnapshotEnvelope { public ObjectStateSnapshot? State { get; set; } }
[SerializationDataAdapter(typeof(BoxAdapter))]
public sealed class ValueBox(int value) { public int Value { get; } = value; }
public sealed class BoxData { public int Value { get; set; } }
public sealed class BoxAdapter : SerializationDataAdapter
{
    public override Type ObjectType => typeof(ValueBox); public override Type DataType => typeof(BoxData);
    public override object CaptureData(object value) => new BoxData { Value = ((ValueBox)value).Value };
    public override object RestoreData(object data) => new ValueBox(((BoxData)data).Value);
}
[SerializationDataAdapter(typeof(BoxAdapter))] public sealed class BadBox;
[SerializationDataAdapter(typeof(CycleAdapter))] public sealed class CycleBox;
public sealed class CycleAdapter : SerializationDataAdapter
{
    public override Type ObjectType => typeof(CycleBox); public override Type DataType => typeof(ValueBox);
    public override object CaptureData(object value) => throw new NotSupportedException();
    public override object RestoreData(object data) => throw new NotSupportedException();
}
[SerializationDataAdapter(typeof(DepthAdapter))] public sealed class DepthBox(DepthData data) { public DepthData Data { get; } = data; }
public sealed class DepthData { public DepthData? Next { get; set; } public int Value { get; set; } }
public sealed class DepthAdapter : SerializationDataAdapter
{
    public override Type ObjectType => typeof(DepthBox); public override Type DataType => typeof(DepthData);
    public override object CaptureData(object value) => ((DepthBox)value).Data;
    public override object RestoreData(object data) => new DepthBox((DepthData)data);
}
public sealed class ForeignConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => false;
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) => throw new NotSupportedException();
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) => throw new NotSupportedException();
}
