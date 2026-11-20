using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.UIElements;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action call, string name) where T : Exception
{ try { call(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform()
    .AddSingleton(new GraphicsUtility(new DrawingPlatform())).BuildServiceProvider();
PlatformRuntime.Initialize(services);
RuntimeHelpers.RunModuleConstructor(typeof(UIDocument).Module.ModuleHandle);
var graphics = typeof(RenderSubmission).Assembly;
Check(typeof(IDrawContributor).Assembly == graphics, "Neutral drawing contract belongs to Graphics");
foreach (var name in new[] { "BEngine.Rendering.RenderCamera", "BEngine.Rendering.SceneQuad2D", "BEngine.RenderBatchBuilder2D", "BEngine.Rendering.ISceneRenderContributor2D", "BEngine.CameraClearMode" })
    Check(graphics.GetType(name) is null, name + " is absent from Graphics");
Check(!graphics.GetReferencedAssemblies().Any(a => a.Name == "BEngine.Rendering2D"), "Graphics has no scene-renderer assembly reference");
Check(!typeof(UIDocument).Assembly.GetReferencedAssemblies().Any(a => a.Name == "BEngine.Rendering2D"), "UIElements has no scene-renderer assembly reference");
foreach (var (old, current) in new[] { ("RenderSortKey2D", typeof(RenderOrderKey)), ("RenderBatchKey2D", typeof(RenderBatchKey)), ("RenderSubmission2D", typeof(RenderSubmission)), ("RenderBatch2D", typeof(RenderBatch)) })
    foreach (var suffix in new[] { "", ", BEngine.Graphics", ", BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null" })
        Check(RuntimeTypeCache.FindType("BEngine." + old + suffix) == current, "Legacy packet alias " + old + suffix);
var packet = new RenderSubmission(new(SortingLayer.Ui, 0, 0, RenderTransparency.Opaque, 0), new(Guid.Empty, "probe", ""), new object());
object indexedPacket = packet;
typeof(RenderSubmission).GetProperty("FramePayloadIndex", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(indexedPacket, 15);
Check(packet == (RenderSubmission)indexedPacket && packet.GetHashCode() == indexedPacket.GetHashCode(), "Host payload indices do not change public packet equality or hashing");
for (ulong layer = 0; layer < 256; layer++)
{
    var offset = unchecked(layer - (ulong)SortingLayer.MinimumIndex);
    var expected = offset <= (ulong)(SortingLayer.MaximumIndex - SortingLayer.MinimumIndex) && (SortingLayer.AllMask & (1UL << (int)offset)) != 0;
    if (RenderLayerMask.Contains(SortingLayer.AllMask, layer) != expected) throw new Exception("Layer mask changed");
}
Check(!RenderLayerMask.Contains(0, SortingLayer.Ui) && !RenderLayerMask.Contains(ulong.MaxValue, ulong.MaxValue), "Layer masks preserve bounds and exclusion");
using var device = new RecordingDevice();
var snapshots = RenderContributorRegistry.GetContributors(device);
var ui = snapshots.ToArray().OfType<IDrawContributor>().Single(c => c.packageId == "com.bengine.ui-elements");
Check(ui.rendersUi, "UI registers a neutral drawing contributor");
using var scene = new Scene("Neutral UI", services);
var document = scene.CreateGameObject("Document").AddComponent<UIDocument>();
var root = document.rootVisualElement;
root.style.width = 160; root.style.height = 90; root.style.backgroundColor = new UIColor(70, 80, 90);
var child = new VisualElement(); child.style.width = 40; child.style.height = 20; child.style.backgroundColor = new UIColor(200, 100, 60); root.Add(child);
var context = new RenderCollectionContext(scene, 640, 360, SortingLayer.AllMask);
var submissions = new List<RenderSubmission>();
ui.BeginFrame(); ui.BeginScene(context); var order = ui.Collect(context, submissions, 17);
Check(submissions.Count >= 2 && order == 17 + submissions.Count, "UI collects actual commands without a camera");
Check(submissions.Select(s => s.SortKey.SubmissionOrder).SequenceEqual(Enumerable.Range(17, submissions.Count).Select(i => (long)i)), "Neutral UI preserves submission order");
using var uiBatches = new RenderBatchBuffer();
uiBatches.BuildOrdered(submissions);
Check(uiBatches.Count > 0 && uiBatches.All(ui.CanRender), "Neutral UI recognizes its command payloads");
foreach (var batch in uiBatches) ui.Render(batch, 640, 360, new GraphicsRect(0, 0, 640, 360));
uiBatches.Clear();
Check(device.Draws > 0 && device.Vertices >= 12, "Neutral UI draws rectangles through the RHI");
ui.EndFrame();
ui.BeginFrame(); submissions.Clear();
Check(ui.Collect(context with { ObjectFilter = static _ => false }, submissions, 5) == 5 && submissions.Count == 0, "Neutral UI respects the object filter");
ui.EndFrame();
document.cullExcludedCameraSubmissions = true;
ui.BeginFrame(); submissions.Clear();
Check(ui.Collect(context with { CullingMask = 0 }, submissions, 0) >= 2 && submissions.Count == 0, "Opt-in UI layer culling advances order without emitting commands");
ui.EndFrame(); document.cullExcludedCameraSubmissions = false;
var callbackItems = new CallbackCollection(() => document.sortingOrder = 11);
ui.BeginFrame(); ui.Collect(context, callbackItems, 0);
Check(callbackItems.Items.Count >= 2 && callbackItems.Items[0].SortKey.GroupOrder == 0 &&
    callbackItems.Items.Skip(1).All(s => s.SortKey.GroupOrder == 11), "Custom collection callbacks retain live per-command document metadata");
ui.EndFrame(); document.sortingOrder = 0;
for (var i = 0; i < 20; i++) { ui.BeginFrame(); submissions.Clear(); ui.Collect(context, submissions, 0); ui.EndFrame(); }
var watch = Stopwatch.StartNew(); var allocated = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < 1000; i++) { ui.BeginFrame(); submissions.Clear(); ui.Collect(context, submissions, 0); ui.EndFrame(); }
var collectMilliseconds = watch.Elapsed.TotalMilliseconds; var collectBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
Check(submissions.Count >= 2, "Warm UI collection retains commands");

var first = new Probe("first"); var second = new Probe("second");
RenderContributorRegistry.Register("!boundary-b", _ => second);
RenderContributorRegistry.Register("!boundary-a", _ => first);
snapshots = RenderContributorRegistry.GetContributors(device);
Check(ReferenceEquals(snapshots[0], first) && ReferenceEquals(snapshots[1], second), "Contributors use ordinal registration order across kinds");
Check(ReferenceEquals(snapshots[0], RenderContributorRegistry.GetContributors(device)[0]), "Device contributor snapshots reuse instances");
_ = MeasureSnapshots(device); _ = MeasureSnapshots(device);
var snapshotBytes = MeasureSnapshots(device);
Console.WriteLine($"SNAPSHOT_BYTES|{snapshotBytes}");
Check(snapshotBytes == 0, "10000 warm contributor snapshots allocate zero bytes");
var replacement = new Probe("replacement");
RenderContributorRegistry.Register("!boundary-a", _ => replacement);
Check(first.Disposals == 1 && ReferenceEquals(RenderContributorRegistry.GetContributors(device)[0], replacement), "Replacing a registration disposes the previous instance exactly once");
Check(RenderContributorRegistry.Unregister("!boundary-a") && replacement.Disposals == 1 && !RenderContributorRegistry.Unregister("!boundary-a"), "Unregister disposes once and reports absence");
RenderContributorRegistry.Unregister("!boundary-b"); Check(second.Disposals == 1, "Other cached contributors are released");
RenderContributorRegistry.Register("!boundary-device", _ => new Probe("device-specific"));
using (var otherDevice = new RecordingDevice())
{
    var left = RenderContributorRegistry.GetContributors(device).ToArray().Single(c => c.packageId == "device-specific");
    var right = RenderContributorRegistry.GetContributors(otherDevice).ToArray().Single(c => c.packageId == "device-specific");
    Check(!ReferenceEquals(left, right), "Contributor instances are isolated by device identity");
    RenderContributorRegistry.Release(otherDevice);
    Check(((Probe)right).Disposals == 1 && ((Probe)left).Disposals == 0 && otherDevice.ResourcesCreated == otherDevice.ResourcesDisposed, "Releasing one device preserves other devices and releases its UI resources");
}
RenderContributorRegistry.Unregister("!boundary-device");
var reentrant = false;
RenderContributorRegistry.Register("!boundary-reentrant", _ => { if (!reentrant) { reentrant = true; RenderContributorRegistry.Register("!boundary-late", _ => new Probe("late")); } return new Probe("reentrant"); });
snapshots = RenderContributorRegistry.GetContributors(device);
Check(snapshots.ToArray().Any(c => c.packageId == "late") && snapshots.ToArray().All(c => c is not null), "Factory-triggered registrations publish a complete stable snapshot");
RenderContributorRegistry.Unregister("!boundary-reentrant"); RenderContributorRegistry.Unregister("!boundary-late");
RenderContributorRegistry.Register("!boundary-null", _ => null!);
Throws<InvalidOperationException>(() => { _ = RenderContributorRegistry.GetContributors(device).Length; }, "Null contributor factories fail clearly");
RenderContributorRegistry.Unregister("!boundary-null");
var originalOwners = new List<Probe>(); var otherOwner = new Probe("other-owner");
RenderContributorRegistry.Register("!boundary-owner", _ => { var item = new Probe("original-owner"); originalOwners.Add(item); return item; });
_ = RenderContributorRegistry.GetContributors(device).Length;
var foreignFactory = ForeignFactory(_ => otherOwner);
RenderContributorRegistry.Register("!boundary-owner", foreignFactory);
Check(originalOwners[0].Disposals == 1 && ReferenceEquals(RenderContributorRegistry.GetContributors(device)[0], otherOwner), "An assembly override disposes the previous active contributor");
RenderContributorRegistry.UnregisterAssembly(foreignFactory.Method.DeclaringType!.Assembly);
Check(otherOwner.Disposals == 1 && RenderContributorRegistry.GetContributors(device)[0].packageId == "original-owner" && originalOwners.Count == 2 && originalOwners[1].Disposals == 0, "Assembly unload restores the previous factory owner with a fresh instance");
RenderContributorRegistry.Unregister("!boundary-owner");
var bad = new Probe("throws", true);
RenderContributorRegistry.Register("!boundary-throws", _ => bad);
_ = RenderContributorRegistry.GetContributors(device).Length;
Throws<AggregateException>(() => RenderContributorRegistry.Unregister("!boundary-throws"), "Disposal failures are aggregated");
Check(bad.Disposals == 1 && !RenderContributorRegistry.GetContributors(device).ToArray().Contains(bad), "Throwing disposal still removes cached instances");

#if SCENE
foreach (var type in new[] { typeof(RenderCamera), typeof(SceneQuad2D), typeof(SceneQuadPayload2D), typeof(RenderViewportTransform2D), typeof(SceneRenderContext2D), typeof(CameraClearMode) })
{
    Check(type.Assembly == typeof(PortableSceneRenderer).Assembly, type.Name + " belongs to Rendering2D");
    foreach (var suffix in new[] { ", BEngine.Graphics", ", BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null" })
        Check(RuntimeTypeCache.FindType(type.FullName + suffix) == type, "Moved type alias " + type.Name + suffix);
}
Func<string, Type?> resolvePersisted = RuntimeTypeCache.ResolveSerializedType;
foreach (var type in new[] { typeof(RenderCamera), typeof(CameraClearMode), typeof(SceneQuad2D), typeof(SceneQuadPayload2D), typeof(RenderViewportTransform2D), typeof(SceneRenderContext2D), typeof(RenderBatchBuilder2D), typeof(SceneRenderContributor2DRegistry), typeof(ISceneRenderContributor2D), typeof(IRenderCameraPolicy) })
    Check(resolvePersisted(type.FullName + ", BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null") == type, "Persisted old assembly identity resolves " + type.Name);
Check(resolvePersisted("System.Collections.Generic.List`1[[BEngine.Rendering.RenderCamera, BEngine.Graphics]], System.Private.CoreLib") == typeof(List<RenderCamera>), "Persisted generic arguments resolve the moved camera identity");
Check(resolvePersisted("BEngine.Rendering.ISceneRenderContributor2D[], BEngine.Graphics") == typeof(ISceneRenderContributor2D[]), "Persisted interface arrays resolve after assembly relocation");
var world = new WorldProbe(); SceneRenderContributor2DRegistry.Register("!boundary-world", _ => world);
using (var renderer = new PortableSceneRenderer(device))
{
    renderer.Render(scene, RenderCamera.Default, 640, 360);
    Check(world.Begins == 1 && world.Ends == 1 && world.Scenes == 1 && world.Collections == 1, "Scene host preserves world contributor callbacks");
    var beforeDraws = device.Draws;
    renderer.Render(scene, RenderCamera.Default, 640, 360);
    Check(device.Draws > beforeDraws, "Scene host renders neutral UI through the shared registry");
    renderer.Render(scene, RenderCamera.Default, 640, 360, drawUi: false);
    Check(world.Collections == 3, "World contributors remain active when UI is disabled");
    for (var i = 0; i < 100; i++) renderer.Render(scene, RenderCamera.Default, 640, 360);
    Check(world.Begins == world.Ends && world.Collections == 103, "Repeated scene frames balance contribution lifetime");
}
Check(SceneRenderContributor2DRegistry.Unregister("!boundary-world") && world.Disposals == 1, "World wrapper unregisters the caller-owned shared registration");
var key = new RenderBatchKey(Guid.NewGuid(), "test", "");
var commands = new[] {
    new RenderSubmission(new(SortingLayer.Ui, 1, 0, RenderTransparency.Transparent, 2), key, "last"),
    new RenderSubmission(new(SortingLayer.Ui, 0, 0, RenderTransparency.Transparent, 1), key, "first"),
    new RenderSubmission(new(SortingLayer.Ui, 0, 0, RenderTransparency.Transparent, 1), key, "equal") };
var batches = RenderBatchBuilder2D.Build(commands);
Check(batches.Count == 1 && batches[0].Submissions.Select(s => s.Payload).SequenceEqual(new[] { "first", "equal", "last" }), "Moved scene batch builder preserves stable sort and batch merging");
#else
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BEngine.Rendering2D"), "UI collection and actual RHI drawing do not load Rendering2D");
#endif
RenderContributorRegistry.Release(device);
Check(device.ResourcesCreated == device.ResourcesDisposed, "All contributor and renderer GPU resources are released");
var result = new { Passed = true, Checks = checks.Count, Details = checks, SnapshotAllocatedBytes = snapshotBytes, WarmUiCollectIterations = 1000, WarmUiCollectMilliseconds = collectMilliseconds, WarmUiCollectAllocatedBytes = collectBytes, device.Draws, device.Vertices };
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));

[MethodImpl(MethodImplOptions.NoInlining)]
static long MeasureSnapshots(IGraphicsDevice device)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var index = 0; index < 10000; index++) _ = RenderContributorRegistry.GetContributors(device).Length;
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

static Func<IGraphicsDevice, IRenderContributor> ForeignFactory(Func<IGraphicsDevice, IRenderContributor> callback)
{
    var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ContributorOwnerProbe"), AssemblyBuilderAccess.Run);
    var type = assembly.DefineDynamicModule("Owner").DefineType("ForeignOwner", TypeAttributes.Public);
    var field = type.DefineField("Callback", callback.GetType(), FieldAttributes.Public | FieldAttributes.Static);
    var method = type.DefineMethod("Create", MethodAttributes.Public | MethodAttributes.Static, typeof(IRenderContributor), [typeof(IGraphicsDevice)]);
    var il = method.GetILGenerator(); il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, callback.GetType().GetMethod("Invoke")!); il.Emit(OpCodes.Ret);
    var created = type.CreateType()!; created.GetField("Callback")!.SetValue(null, callback);
    return created.GetMethod("Create")!.CreateDelegate<Func<IGraphicsDevice, IRenderContributor>>();
}

sealed class CallbackCollection(Action firstAdd) : ICollection<RenderSubmission>
{
    public readonly List<RenderSubmission> Items = [];
    public int Count => Items.Count;
    public bool IsReadOnly => false;
    public void Add(RenderSubmission item) { Items.Add(item); if (Items.Count == 1) firstAdd(); }
    public void Clear() => Items.Clear();
    public bool Contains(RenderSubmission item) => Items.Contains(item);
    public void CopyTo(RenderSubmission[] items, int index) => Items.CopyTo(items, index);
    public bool Remove(RenderSubmission item) => Items.Remove(item);
    public IEnumerator<RenderSubmission> GetEnumerator() => Items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

class Probe(string name, bool failDisposal = false) : IRenderContributor
{
    public string packageId => name;
    public int Disposals;
    public bool CanRender(RenderBatch batch) => false;
    public void Dispose() { Disposals++; if (failDisposal) throw new InvalidOperationException("Expected test disposal"); }
}
#if SCENE
sealed class WorldProbe() : Probe("world"), ISceneRenderContributor2D
{
    public int Begins, Ends, Scenes, Collections;
    public bool processesQuads => false;
    public void BeginFrame() => Begins++;
    public void EndFrame() => Ends++;
    public void BeginScene(Scene scene) => Scenes++;
    public long Collect(Scene scene, RenderCamera camera, int w, int h, ICollection<RenderSubmission> items, long order) { Collections++; return order; }
    public void Render(RenderBatch batch, RenderCamera camera, int w, int h, GraphicsRect viewport) { }
}
#endif
