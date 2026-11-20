using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Rendering;
using BEngine.Platform.DotNet;
#if SCENE
using BEngine.Platform;
using BEngine.Rendering.Rhi;
using BEngine.UIElements;
using Microsoft.Extensions.DependencyInjection;
#endif

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
DotNetPlatformServices.Initialize();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
static SceneRenderDiagnosticSample Row(SceneRenderDiagnosticSnapshot snapshot, string name, bool renderer = false) =>
    snapshot.Samples.Single(r => r.Name == name && r.RendererType == renderer);
static SceneRenderDiagnosticSnapshot Complete(ref RenderDiagnostics.CaptureContext? context, RenderDiagnosticPhase phase)
{
    using var capture = RenderDiagnostics.BeginCapture(ref context);
    using (RenderDiagnostics.Measure(phase)) { }
    var snapshot = RenderDiagnostics.CaptureStatistics(1)!; capture.CleanupCompleted(); return snapshot;
}
var graphics = typeof(RenderDiagnostics).Assembly;
Check(graphics.GetType("BEngine.Rendering.SceneRenderDiagnosticPhase") is null, "Graphics has no concrete scene/UI phase enum");
Check(graphics.GetType("BEngine.Rendering.SceneRenderDiagnosticPhases") is null && graphics.GetType("BEngine.Rendering.UIRenderDiagnosticPhases") is null,
    "Graphics has no scene/UI phase catalogs");
Check(!graphics.GetReferencedAssemblies().Any(a => a.Name is "BEngine.Rendering2D" or "BEngine.UIElements" or "BEngine.Diagnostics"),
    "The sampler has no scene/UI/diagnostics implementation dependency");
Check(RuntimeTypeCache.FindType("BEngine.Rendering.SceneRenderDiagnostics, BEngine.Graphics") == typeof(RenderDiagnostics), "The legacy collector type identity resolves");
Throws<ArgumentException>(() => _ = new RenderDiagnosticPhase(" "), "Empty phase names are rejected");
Throws<ArgumentNullException>(() => _ = new RenderDiagnosticPhase(null!), "Null phase names are rejected");
Check(new RenderDiagnosticPhase("Custom.Stage") == new RenderDiagnosticPhase("Custom.Stage") &&
    new RenderDiagnosticPhase("Custom.Stage") != new RenderDiagnosticPhase("custom.stage"), "Phase identity is ordinal");

RenderDiagnostics.CaptureContext? context = null;
var phase = new RenderDiagnosticPhase("Custom.Stage");
SceneRuntimeDiagnostics.Enabled = false;
_ = MeasureDisabled(ref context, phase); _ = MeasureDisabled(ref context, phase);
var clockBefore = RenderDiagnostics.ClockReadCount; var allocationsBefore = RenderDiagnostics.AllocationReadCount;
var typesBefore = RenderDiagnostics.TypeNameReadCount;
var (disabledBytes, disabledMs) = MeasureDisabled(ref context, phase);
[MethodImpl(MethodImplOptions.NoInlining)]
static (long Bytes, double Milliseconds) MeasureDisabled(ref RenderDiagnostics.CaptureContext? context, RenderDiagnosticPhase phase)
{
    var started = Stopwatch.GetTimestamp(); var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100_000; i++)
    {
        using var c = RenderDiagnostics.BeginCapture(ref context);
        using var m = RenderDiagnostics.Measure(phase);
        using var r = RenderDiagnostics.MeasureRenderer(null!);
        if (RenderDiagnostics.CaptureStatistics(0) is not null) throw new Exception("Disabled snapshot");
    }
    return (GC.GetAllocatedBytesForCurrentThread() - bytesBefore, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}
Console.WriteLine($"DISABLED|bytes={disabledBytes}|context={context is not null}");
Check(context is null && disabledBytes == 0, "100000 disabled captures allocate no buffer or managed bytes");
Check(RenderDiagnostics.ClockReadCount == clockBefore && RenderDiagnostics.AllocationReadCount == allocationsBefore && RenderDiagnostics.TypeNameReadCount == typesBefore,
    "Disabled sampling reads no clock, allocation counter or renderer type");

using var recorder = SceneRuntimeDiagnostics.RegisterRecorder(new Recorder());
SceneRuntimeDiagnostics.Enabled = true;
Check(RenderDiagnostics.Enabled, "The optional Core recorder enables drawing sampling without the Diagnostics package");
SceneRenderDiagnosticSnapshot snapshot;
using (var capture = RenderDiagnostics.BeginCapture(ref context))
{
    using (RenderDiagnostics.Measure(default)) { }
    using (RenderDiagnostics.Measure(phase))
    {
        using (RenderDiagnostics.Measure(new RenderDiagnosticPhase("Custom.Child"))) { GC.KeepAlive(new byte[1000]); }
    }
    using (RenderDiagnostics.Measure(new RenderDiagnosticPhase("Custom.Stage"))) { }
    using (RenderDiagnostics.Measure(new RenderDiagnosticPhase("custom.stage"))) { }
    using (RenderDiagnostics.Measure(new RenderDiagnosticPhase(typeof(Recorder).FullName!))) { }
    using (RenderDiagnostics.MeasureRenderer(new Recorder())) { }
    snapshot = RenderDiagnostics.CaptureStatistics(2)!; capture.CleanupCompleted();
}
Check(snapshot is { Completed: true, BeforeCleanup: true, CameraCount: 2, RenderDepth: 1, OpenScopes: 0, DroppedScopes: 0 }, "Complete capture preserves the shared statistics envelope");
Check(Row(snapshot, "Custom.Stage").Calls == 2 && Row(snapshot, "custom.stage").Calls == 1, "Equal names merge while case-distinct names stay separate");
Check(snapshot.Samples.Length == 5, "Default phases are ignored and renderer/phase slots are independent");
Check(Row(snapshot, typeof(Recorder).FullName!, true).Calls == 1 && Row(snapshot, typeof(Recorder).FullName!).Calls == 1, "Renderer and phase rows can share a label");
Check(Row(snapshot, "Custom.Child").AllocatedBytes >= 1000 && Row(snapshot, "Custom.Stage").AllocatedBytes >= Row(snapshot, "Custom.Child").AllocatedBytes,
    "Nested allocation measurements preserve inclusive costs");
Check(snapshot.Samples.All(r => r.SelfTicks <= r.InclusiveTicks && r.SelfAllocatedBytes <= r.AllocatedBytes && r.SelfTicks >= 0 && r.SelfAllocatedBytes >= 0), "Self costs are bounded by inclusive costs");
Check(snapshot.TotalTicks == snapshot.UnattributedTicks + snapshot.NestedRenderTicks + snapshot.Samples.Sum(r => r.SelfTicks), "CPU self/nested/unattributed ledger sums to the capture total");
Check(snapshot.TotalAllocatedBytes == snapshot.UnattributedAllocatedBytes + snapshot.NestedRenderAllocatedBytes + snapshot.Samples.Sum(r => r.SelfAllocatedBytes), "Allocation ledger sums to the capture total");
var reset = Complete(ref context, new("Next.Frame"));
Check(reset.Samples.Length == 1 && reset.Samples[0].Name == "Next.Frame" && reset.DroppedScopes == 0, "Reusing a context releases prior labels and counters");

using (var capture = RenderDiagnostics.BeginCapture(ref context))
{
    for (var i = 0; i < 70; i++) { using var m = RenderDiagnostics.Measure(new($"Custom.{i}")); }
    snapshot = RenderDiagnostics.CaptureStatistics(0)!; capture.CleanupCompleted();
}
Check(snapshot.Samples.Length == RenderDiagnostics.MaximumPhases + 1 && snapshot.OverflowPhaseOccurrences == 6 && Row(snapshot, "<other-render-phases>").Calls == 6,
    "Custom phase cardinality is bounded at 64 with explicit overflow accounting");
Check(Complete(ref context, phase).OverflowPhaseOccurrences == 0, "Phase overflow resets on the next capture");

var dynamicModule = AssemblyBuilder.DefineDynamicAssembly(new("DiagnosticBoundaryTypes"), AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Types");
var rendererObjects = Enumerable.Range(0, 70).Select(i => Activator.CreateInstance(dynamicModule.DefineType($"Renderer{i}", System.Reflection.TypeAttributes.Public).CreateType()!)!).ToArray();
using (var capture = RenderDiagnostics.BeginCapture(ref context))
{
    foreach (var target in rendererObjects) { using var m = RenderDiagnostics.MeasureRenderer(target); }
    snapshot = RenderDiagnostics.CaptureStatistics(0)!; capture.CleanupCompleted();
}
Check(snapshot.Samples.Length == RenderDiagnostics.MaximumRendererTypes + 1 && snapshot.OverflowRendererTypeOccurrences == 6 && Row(snapshot, "<other-renderer-types>", true).Calls == 6,
    "Renderer type cardinality remains bounded and separate from phases");

var measures = new RenderDiagnostics.Measurement[65];
using (var capture = RenderDiagnostics.BeginCapture(ref context))
{
    for (var i = 0; i < measures.Length; i++) measures[i] = RenderDiagnostics.Measure(phase);
    for (var i = measures.Length - 1; i >= 0; i--) measures[i].Dispose();
    snapshot = RenderDiagnostics.CaptureStatistics(0)!; capture.CleanupCompleted();
}
Check(snapshot.OpenScopes == 0 && snapshot.DroppedScopes == 1 && Row(snapshot, phase.Name!).Calls == 64, "Scope depth overflow fails closed and unwinds correctly");

using (var capture = RenderDiagnostics.BeginCapture(ref context))
{
    var first = RenderDiagnostics.Measure(phase); var stale = first; first.Dispose();
    var second = RenderDiagnostics.Measure(phase); stale.Dispose(); second.Dispose();
    var parent = RenderDiagnostics.Measure(new("Parent")); var child = RenderDiagnostics.Measure(new("Child"));
    parent.Dispose(); child.Dispose(); parent.Dispose();
    snapshot = RenderDiagnostics.CaptureStatistics(0)!; capture.CleanupCompleted();
}
Check(Row(snapshot, phase.Name!).Calls == 2 && snapshot.DroppedScopes == 1 && snapshot.OpenScopes == 0 && Row(snapshot, "Parent").Calls == 1,
    "Copied/disposed measurement tokens cannot close later scopes; out-of-order scopes recover");
var oldCapture = RenderDiagnostics.BeginCapture(ref context);
var oldMeasure = RenderDiagnostics.Measure(phase); oldMeasure.Dispose();
_ = RenderDiagnostics.CaptureStatistics(0); oldCapture.CleanupCompleted(); oldCapture.Dispose();
using (var fresh = RenderDiagnostics.BeginCapture(ref context))
{
    oldCapture.Dispose(); oldCapture.CleanupCompleted(); oldMeasure.Dispose();
    using (RenderDiagnostics.Measure(new("Fresh"))) { }
    snapshot = RenderDiagnostics.CaptureStatistics(0)!; fresh.CleanupCompleted();
}
Check(snapshot.Samples.Length == 1 && snapshot.Samples[0].Name == "Fresh" && snapshot.DroppedScopes == 0, "Generation tokens reject stale captures and measurements after context reuse");

RenderDiagnostics.CaptureContext? childContext = null;
using (var parentCapture = RenderDiagnostics.BeginCapture(ref context))
{
    var parentScope = RenderDiagnostics.Measure(new("Parent.Render"));
    using (var childCapture = RenderDiagnostics.BeginCapture(ref childContext))
    {
        parentScope.Dispose(); parentCapture.Dispose();
        using (RenderDiagnostics.Measure(new("Child.Render"))) { GC.KeepAlive(new byte[2000]); }
        var childSnapshot = RenderDiagnostics.CaptureStatistics(1)!; childCapture.CleanupCompleted();
        Check(childSnapshot.RenderDepth == 2 && childSnapshot.Samples.Length == 1 && childSnapshot.DroppedScopes == 0, "Nested captures isolate their rows and depth");
    }
    parentScope.Dispose(); snapshot = RenderDiagnostics.CaptureStatistics(2)!; parentCapture.CleanupCompleted();
}
Check(snapshot.DroppedScopes == 2 && snapshot.OpenScopes == 0 && snapshot.NestedRenderAllocatedBytes >= 2000 && snapshot.Samples.Length == 1,
    "Parent disposal cannot corrupt an active child capture");
Check(snapshot.TotalTicks == snapshot.UnattributedTicks + snapshot.NestedRenderTicks + snapshot.Samples.Sum(r => r.SelfTicks), "Nested capture CPU costs are counted once");

var contexts = new RenderDiagnostics.CaptureContext?[10];
var captures = new RenderDiagnostics.CaptureScope[10];
for (var i = 0; i < captures.Length; i++) captures[i] = RenderDiagnostics.BeginCapture(ref contexts[i]);
Check(contexts.Take(8).All(c => c is not null) && contexts.Skip(8).All(c => c is null), "Capture depth is bounded at eight buffers");
var staleSuppressed = captures[9]; captures[9].Dispose(); staleSuppressed.Dispose();
Check(RenderDiagnostics.CaptureStatistics(0) is null, "A duplicate suppressed token does not prematurely enable parent sampling");
captures[8].Dispose(); staleSuppressed.Dispose();
Check(RenderDiagnostics.CaptureStatistics(0)?.RenderDepth == 8, "Suppression unwinds to the valid parent");
for (var i = 7; i >= 0; i--) { _ = RenderDiagnostics.CaptureStatistics(0); captures[i].CleanupCompleted(); captures[i].Dispose(); }
Check(RenderDiagnostics.CaptureStatistics(0) is null, "Depth-limited captures leave no active state");
using (var capture = RenderDiagnostics.BeginCapture(ref context))
{
    using var suppressed = RenderDiagnostics.BeginCapture(ref context);
    Check(RenderDiagnostics.CaptureStatistics(0) is null, "Reusing an active context suppresses the recursive capture");
}
Check(RenderDiagnostics.CaptureStatistics(0) is null && Complete(ref context, phase).Samples.Length == 1, "Recursive context suppression recovers cleanly");
var expectedException = new InvalidOperationException("render-failure");
try
{
    using var capture = RenderDiagnostics.BeginCapture(ref context);
    using var measurement = RenderDiagnostics.Measure(new("Exception"));
    throw expectedException;
}
catch (InvalidOperationException exception) { Check(ReferenceEquals(exception, expectedException), "Diagnostic cleanup preserves the original rendering exception"); }
Check(RenderDiagnostics.LastIncompleteCapture is { Completed: false, OpenScopes: 0 } incomplete && Row(incomplete, "Exception").Calls == 1,
    "Incomplete captures remain available for failure diagnosis");
Check(RenderDiagnostics.CaptureStatistics(0) is null, "Exception unwinding releases the active capture");
snapshot = Complete(ref context, phase);
var serialized = JsonSerializer.Serialize(snapshot);
Check(JsonSerializer.Deserialize<SceneRenderDiagnosticSnapshot>(serialized)!.Samples.SequenceEqual(snapshot.Samples), "Shared diagnostic snapshots round-trip through JSON");
Check(JsonSerializer.Deserialize<SceneRenderDiagnosticSnapshot>("{\"Completed\":true,\"Samples\":[]}")!.OverflowPhaseOccurrences == 0, "Older snapshot JSON defaults the new overflow field");

// Immutable snapshots are intentionally excluded: their ownership transfers to consumers.
for (var i = 0; i < 1000; i++)
{
    using var capture = RenderDiagnostics.BeginCapture(ref context);
    using (RenderDiagnostics.Measure(phase)) { }
    _ = RenderDiagnostics.CaptureStatistics(0); capture.CleanupCompleted();
}
// Mark the snapshot state reflectively so the benchmark isolates reusable capture/scope bookkeeping.
var bufferField = typeof(RenderDiagnostics.CaptureContext).GetField("Buffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
var buffer = bufferField.GetValue(context)!;
var snapshotField = buffer.GetType().GetField("Snapshotted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
var setter = new DynamicMethod("MarkTransferredSnapshot", typeof(void), [typeof(object)], typeof(Program).Module, true);
var il = setter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, buffer.GetType());
il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stfld, snapshotField); il.Emit(OpCodes.Ret);
var markSnapshot = setter.CreateDelegate<Action<object>>();
_ = MeasureWarm(context, phase, markSnapshot, buffer); _ = MeasureWarm(context, phase, markSnapshot, buffer);
var (warmUnboxedBytes, warmMs) = MeasureWarm(context, phase, markSnapshot, buffer);
[MethodImpl(MethodImplOptions.NoInlining)]
static (long Bytes, double Milliseconds) MeasureWarm(RenderDiagnostics.CaptureContext? context, RenderDiagnosticPhase phase, Action<object> markSnapshot, object buffer)
{
    var started = Stopwatch.GetTimestamp(); var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++)
    {
        using var capture = RenderDiagnostics.BeginCapture(ref context);
        using (RenderDiagnostics.Measure(phase)) { }
        markSnapshot(buffer); capture.CleanupCompleted();
    }
    return (GC.GetAllocatedBytesForCurrentThread() - bytesBefore, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}
Console.WriteLine($"WARM|bytes={warmUnboxedBytes}");
Check(warmUnboxedBytes == 0, "10000 warm reusable captures and custom phases allocate zero bytes apart from owned snapshots");

#if SCENE
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton(new GraphicsUtility(new DrawingPlatform())).BuildServiceProvider();
PlatformRuntime.Initialize(services);
RuntimeHelpers.RunModuleConstructor(typeof(UIDocument).Module.ModuleHandle);
Check(typeof(SceneRenderDiagnosticPhases).Assembly == typeof(SpriteRenderer).Assembly && typeof(UIRenderDiagnosticPhases).Assembly == typeof(UIDocument).Assembly,
    "Scene/UI phase catalogs belong to their respective packages");
using var scene = new Scene("Sampled drawing", services);
var document = scene.CreateGameObject("UI").AddComponent<UIDocument>(); var root = document.rootVisualElement;
root.style.width = 1280; root.style.height = 720;
for (var i = 0; i < 500; i++)
{
    var element = new VisualElement(); element.style.position = Position.Absolute;
    element.style.left = i % 25 * 45; element.style.top = i / 25 * 30; element.style.width = 40; element.style.height = 25;
    element.style.backgroundColor = new UIColor((byte)(i % 255), 90, 200); root.Add(element);
    var sprite = scene.CreateGameObject("Sprite").AddComponent<SpriteRenderer>(); sprite.size = new Vector2(Fix64.Half, Fix64.Half);
    sprite.transform.position = new Vector2(i % 25 / (Fix64)4 - 3, i / 25 / (Fix64)4 - 2);
}
using var device = new RecordingDevice(); using var renderer = new PortableSceneRenderer(device);
for (var i = 0; i < 10; i++) renderer.Render(scene, RenderCamera.Default, 1280, 720);
var stats = renderer.LastRenderStatistics; var breakdown = stats.RenderBreakdown!;
Check(stats.VisibleSubmissionCount == 1000 && stats.BatchCount == 2, "Enabled diagnostics preserve 500 sprite + 500 UI rendering and batching");
Check(breakdown.Samples.Any(r => r.Name == "Rendering2D.SimpleSprites") && breakdown.Samples.Any(r => r.Name.StartsWith("UI.", StringComparison.Ordinal)),
    "Real scene/UI rendering emits caller-owned diagnostic phases");
Check(breakdown.DroppedScopes == 0 && breakdown.OpenScopes == 0 && breakdown.OverflowPhaseOccurrences == 0, "Real rendering closes every diagnostic scope");
SceneRuntimeDiagnostics.Enabled = false;
for (var i = 0; i < 20; i++) renderer.Render(scene, RenderCamera.Default, 1280, 720);
clockBefore = RenderDiagnostics.ClockReadCount; var before = GC.GetAllocatedBytesForCurrentThread(); var initialDraws = device.Draws;
for (var i = 0; i < 100; i++) renderer.Render(scene, RenderCamera.Default, 1280, 720);
var renderBytes = GC.GetAllocatedBytesForCurrentThread() - before;
Check(renderBytes == 0 && RenderDiagnostics.ClockReadCount == clockBefore && renderer.LastRenderStatistics.RenderBreakdown is null && device.Draws - initialDraws == 200,
    "Disabled sampling adds zero GC or diagnostic clocks to actual rendering and keeps two draws per frame");
#else
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Rendering2D" or "BEngine.UIElements" or "BEngine.Diagnostics"),
    "Generic sampling runs without loading scene/UI/diagnostics packages");
#endif
SceneRuntimeDiagnostics.Enabled = false;
var result = new { Passed = true, Checks = checks.Count, DisabledCalls = 100000, DisabledAllocatedBytes = disabledBytes, DisabledMilliseconds = disabledMs,
    WarmCaptureCount = 10000, WarmAllocatedBytes = warmUnboxedBytes, WarmMilliseconds = warmMs, Details = checks };
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count}");

sealed class Recorder : ISceneRuntimeRecorder
{
    public bool Enabled { get; set; }
    public SceneRuntimeFrameStatistics LastFrame => default;
    public void BeginFrame(long sequence) { }
    public void EndFrame(long sequence) { }
    public SceneRuntimeTickToken BeginTick(int systems) => default;
    public void MoveTick(ref SceneRuntimeTickToken token, SceneRuntimePhase phase) { }
    public void CompleteTick(ref SceneRuntimeTickToken token) { }
    public void RecordDispatchCount(int count) { }
    public void RecordFixedStep() { }
    public void RecordFixedCallback() { }
    public void RecordUpdateCallback() { }
    public void RecordLateCallback() { }
    public long StartFrameSystems() => 0;
    public long StartCallback() => 0;
    public void EndCallback(long started, object target, string callback) { }
    public void EndFrameSystems(long started, bool begin) { }
    public SceneRuntimeKernelToken BeginKernel(string name) => default;
    public void EndKernel(SceneRuntimeKernelToken token) { }
}
