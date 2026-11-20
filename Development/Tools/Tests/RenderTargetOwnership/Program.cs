using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
#if !LEGACY
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
#endif
DotNetPlatformServices.Initialize();
var viewportPlatform = new ViewportPlatform();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton(new GraphicsUtility(viewportPlatform)).BuildServiceProvider();
PlatformRuntime.Initialize(services);
using var device = new ScopedTargetDevice();
using var target = new RenderTexture(8, 8);
#if LEGACY
TrackedTarget Prepare(RenderTexture texture) => (TrackedTarget)typeof(RenderTexture).GetMethod("GetOrCreateTarget", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(texture, [device])!;
#else
TrackedTarget Prepare(RenderTexture texture) { using var scope = RenderTextureScope.Push(device, texture); return device.Current!; }
#endif
Prepare(target); var original = device.Targets.Last();
Prepare(target); Check(ReferenceEquals(original, device.Targets.Last()), "Unchanged descriptor reuses its native resource");
target.format = RenderTextureFormat.R8; Prepare(target);
var formatRecreated = !ReferenceEquals(original, device.Targets.Last());
var afterFormat = device.Targets.Last(); target.useDepth = false; Prepare(target);
var depthRecreated = !ReferenceEquals(afterFormat, device.Targets.Last());
var afterDepth = device.Targets.Last(); target.filterMode = TextureFilterMode.Point; Prepare(target);
var filterRecreated = !ReferenceEquals(afterDepth, device.Targets.Last());

bool nestedRestored = false, nativeRestored = false, failureRestored = false;
#if SCENE
// Two real independent scene renderers on the same RHI, entered from a contribution.
using var scene = new Scene("Target ownership", services);
using var outerTarget = new RenderTexture(8, 8);
using var innerTarget = new RenderTexture(8, 8);
var outerCamera = scene.CreateGameObject("Outer").AddComponent<Camera2D>(); outerCamera.targetTexture = outerTarget;
var innerCamera = scene.CreateGameObject("Inner").AddComponent<Camera2D>(); innerCamera.targetTexture = innerTarget;
var contribution = new NestedDraw();
RenderContributorRegistry.Register("!target-ownership", _ => contribution);
try
{
    using var innerRenderer = new PortableSceneRenderer(device);
    using var outerRenderer = new PortableSceneRenderer(device);
    var recursion = false; bool? activeObserved = null, nativeObserved = null;
    contribution.CollectAction = () =>
    {
        if (recursion) return;
        Check(ReferenceEquals(RenderTexture.active, outerTarget), "Outer real renderer establishes the camera target");
        var native = device.Current; recursion = true;
        try { innerRenderer.RenderCameras([scene], scene, [innerCamera], new(0, 0, 64, 64), false); }
        finally { recursion = false; }
        activeObserved = ReferenceEquals(RenderTexture.active, outerTarget);
        nativeObserved = ReferenceEquals(device.Current, native);
    };
    outerRenderer.RenderCameras([scene], scene, [outerCamera], new(0, 0, 64, 64), false);
    nestedRestored = activeObserved == true; nativeRestored = nativeObserved == true;
    Check(RenderTexture.active is null && device.Current is null, "Outermost render restores the caller surface");
    contribution.CollectAction = () =>
    {
        if (recursion) throw new ExpectedFailure();
        recursion = true;
        try { innerRenderer.RenderCameras([scene], scene, [innerCamera], new(0, 0, 64, 64), false); }
        catch (ExpectedFailure) { }
        finally { recursion = false; }
        activeObserved = ReferenceEquals(RenderTexture.active, outerTarget);
    };
    outerRenderer.RenderCameras([scene], scene, [outerCamera], new(0, 0, 64, 64), false);
    failureRestored = activeObserved == true;
#if !LEGACY
    contribution.CollectAction = null;
    using (RenderTextureScope.Push(device, outerTarget))
    {
        viewportPlatform.EmptyViewport = true;
        innerRenderer.RenderCameras([scene], scene, [innerCamera], new(0, 0, 64, 64), false);
        Check(ReferenceEquals(RenderTexture.active, outerTarget) && innerRenderer.LastRenderStatistics.CameraCount == 0,
            "Empty camera viewport restores caller binding without drawing");
        viewportPlatform.EmptyViewport = false;
        innerCamera.targetTexture = null;
        contribution.CollectAction = () => Check(ReferenceEquals(RenderTexture.active, outerTarget), "Untargeted nested camera inherits the caller resource");
        innerRenderer.RenderCameras([scene], scene, [innerCamera], new(0, 0, 64, 64), false);
        contribution.CollectAction = null;
        Check(ReferenceEquals(RenderTexture.active, outerTarget), "Untargeted camera exit preserves the earlier binding");
        innerCamera.targetTexture = innerTarget;
    }
#endif
}
finally { RenderContributorRegistry.Unregister("!target-ownership"); RenderContributorRegistry.Release(device); }
#else
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Rendering2D" or "BEngine.UIElements" or "BEngine.IMGUI" or "BEngine.EditorWorkbench"),
    "Texture binding works without loading a scene renderer or UI package");
#endif

long warmBytes = -1; double warmMilliseconds = 0;
#if !LEGACY
Check(formatRecreated && depthRecreated && filterRecreated, "Native targets follow color, depth and filtering changes at identical dimensions");
Check(device.Targets.Take(3).All(t => t.Disposals == 1), "Replaced idle targets are released exactly once");
#if SCENE
Check(nestedRestored && nativeRestored && failureRestored, "Nested real renderers restore logical and native targets on success and exception");
#endif
Check(!typeof(RenderTexture).GetProperty("active")!.CanWrite, "Consumers cannot overwrite active resource identity");
Check(typeof(RenderTextureScope).Assembly == typeof(RenderTexture).Assembly, "Resource binding and ownership belong to Graphics");
using var secondDevice = new ScopedTargetDevice();
using var a = new RenderTexture(4, 4); using var b = new RenderTexture(5, 5);
var first = RenderTextureScope.Push(device, a); var firstNative = device.Current!;
Check(first.Width == 4 && first.Height == 4 && ReferenceEquals(RenderTexture.active, a), "Binding returns latched target dimensions");
var second = RenderTextureScope.Push(device, b);
Throws<InvalidOperationException>(() => first.Dispose(), "Out-of-order release is rejected before touching native state");
Check(ReferenceEquals(RenderTexture.active, b), "Rejected disposal preserves the active inner target");
second.Dispose(); second.Dispose();
Check(ReferenceEquals(device.Current, firstNative) && ReferenceEquals(RenderTexture.active, a), "Copied or repeated release restores the outer target once");
using (RenderTextureScope.Push(device, null)) Check(ReferenceEquals(RenderTexture.active, a), "Null target inherits the caller binding on the same device");
using (RenderTextureScope.Push(secondDevice, null)) Check(RenderTexture.active is null, "An unrelated device does not inherit another device identity");
Check(ReferenceEquals(RenderTexture.active, a), "Device nesting restores the earlier identity");
using (var transient = device.CreateRenderTarget("raw", new(2, 2, GraphicsTextureFormat.Rgba8Unorm, null)))
{
    using (RenderTextureScope.PushTransient(device, transient)) Check(RenderTexture.active is null, "Raw intermediate binding hides an unrelated resource identity");
    Check(ReferenceEquals(RenderTexture.active, a), "Raw intermediate binding restores the resource identity");
}
var foreignThread = System.Threading.Tasks.Task.Run(() =>
{
    var independent = RenderTexture.active is null;
    try { first.Dispose(); } catch (InvalidOperationException) { return independent; }
    return false;
}).GetAwaiter().GetResult();
Check(foreignThread && ReferenceEquals(RenderTexture.active, a), "Render-thread state is isolated and a scope cannot be released on another thread");
first.Dispose(); var later = RenderTextureScope.Push(device, b); first.Dispose();
Check(ReferenceEquals(RenderTexture.active, b), "A completed copied scope cannot pop a newer binding at the same depth");
later.Dispose();

// Retired resources remain alive until the last scope stops using them.
var hold = RenderTextureScope.Push(device, a); var retired = device.Current!;
a.Release(); Check(!a.IsCreated() && RenderTexture.active is null && retired.Disposals == 0, "Release detaches logically but defers native disposal while bound");
Throws<InvalidOperationException>(() => RenderTextureScope.Push(device, a), "Released target requires Create");
a.Create(); using (RenderTextureScope.Push(device, a)) Check(!ReferenceEquals(device.Current, retired), "Recreation can bind a fresh resource while a retired one is pinned");
Check(retired.Disposals == 0, "Nested binding does not dispose an outer resource still in use");
hold.Dispose(); Check(retired.Disposals == 1, "Last binding releases a retired native resource");
hold = RenderTextureScope.Push(device, a); retired = device.Current!; a.Resize(6, 7);
Check(retired.Disposals == 0, "Resize defers disposal of the current native target");
using (var resized = RenderTextureScope.Push(device, a)) Check(resized.Width == 6 && resized.Height == 7, "Resize creates the requested new dimensions");
hold.Dispose(); Check(retired.Disposals == 1, "Resize retirement finishes after unbinding");

var existing = Prepare(b); b.format = RenderTextureFormat.R8; device.FailCreate = true;
Throws<ExpectedFailure>(() => Prepare(b), "Creation errors propagate without publishing a replacement");
Check(existing.Disposals == 0 && b.IsCreated() && RenderTexture.active is null, "Creation failure preserves the previous owned resource and binding");
device.FailCreate = false; Prepare(b); Check(existing.Disposals == 1, "Successful retry replaces and releases the earlier descriptor");
device.FailPush = true; Throws<ExpectedFailure>(() => Prepare(b), "Native binding failure propagates"); device.FailPush = false;
Check(RenderTexture.active is null && device.Current is null, "Native binding failure does not publish logical state");
hold = RenderTextureScope.Push(device, b); device.FailPop = true;
Throws<ExpectedFailure>(() => hold.Dispose(), "Native restoration failure leaves the scope retryable");
Check(ReferenceEquals(RenderTexture.active, b), "Failed restore retains the current logical identity"); device.FailPop = false; hold.Dispose();

using (var fallback = new TargetDevice()) using (var ft = new RenderTexture(3, 3))
{
    var fs = RenderTextureScope.Push(fallback, ft); fs.Dispose(); fs.Dispose();
    Check(fallback.Pushes == 1 && fallback.Pops == 1 && RenderTexture.active is null, "IDisposable-only RHI fallback restores exactly once");
}
using (var invalidated = new RenderTexture(2, 2))
{
    hold = RenderTextureScope.Push(device, invalidated); retired = device.Current!;
    invalidated.Dispose(); Check(retired.Disposals == 0 && RenderTexture.active is null, "Destroyed asset is hidden while its active resource remains pinned");
    hold.Dispose(); Check(retired.Disposals == 1, "Destroyed resource is disposed after its binding ends");
    Throws<ObjectDisposedException>(() => invalidated.Resize(1, 1), "Destroyed target cannot resize");
}
Throws<ArgumentNullException>(() => RenderTextureScope.Push(null!, a), "Null device is rejected");
Throws<ArgumentNullException>(() => RenderTextureScope.PushTransient(device, null!), "Null raw target is rejected");
Check(RenderTexture.active is null && device.Pushes == device.Pops, "All successful bindings are balanced");
var sameLinear = Prepare(b); b.filterMode = TextureFilterMode.Trilinear;
Check(ReferenceEquals(sameLinear, Prepare(b)), "Equivalent native linear filtering does not rebuild a target");
hold = RenderTextureScope.Push(device, a); retired = device.Current!;
using (RenderTextureScope.Push(secondDevice, a))
    Check(retired.Disposals == 0 && ReferenceEquals(RenderTexture.active, a), "Device switch pins the previous device resource until unbinding");
Check(retired.Disposals == 0 && RenderTexture.active is null, "Retired device resource cannot masquerade as the newly owned target");
hold.Dispose(); Check(retired.Disposals == 1, "Device-switched resource is released after the last binding");
void Warm() { for (var i = 0; i < 10000; i++) { using var scope = RenderTextureScope.Push(device, a); } }
Warm(); Warm(); var clock = new Stopwatch(); var before = GC.GetAllocatedBytesForCurrentThread(); clock.Start(); Warm(); clock.Stop();
warmBytes = GC.GetAllocatedBytesForCurrentThread() - before; warmMilliseconds = clock.Elapsed.TotalMilliseconds;
Check(warmBytes == 0, "10000 warmed scopes on the token-capable RHI allocate zero bytes");
var released = ReleasedReferences.Make();
for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
Check(released.References.All(w => !w.IsAlive), "Completed scope copies and the thread stack retain no retired texture, native resource or device");
GC.KeepAlive(released.Completed);
#endif
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Tests = checks,
    FormatRecreated = formatRecreated, DepthRecreated = depthRecreated, FilterRecreated = filterRecreated,
    NestedRestored = nestedRestored, NativeRestored = nativeRestored, FailureRestored = failureRestored,
    WarmAllocatedBytes = warmBytes, WarmMilliseconds = warmMilliseconds }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("RENDER_TARGET_OWNERSHIP_OK|" + checks.Count);

#if SCENE
sealed class NestedDraw : IDrawContributor
{
    public Action? CollectAction;
    public string packageId => "target-ownership";
    public bool CanRender(RenderBatch batch) => false;
    public long Collect(in RenderCollectionContext c, ICollection<RenderSubmission> s, long order) { CollectAction?.Invoke(); return order; }
    public void Render(RenderBatch batch, int width, int height, GraphicsRect viewport) { }
    public void Dispose() { }
}
#endif
#if !LEGACY
static class ReleasedReferences
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (WeakReference[] References, RenderTextureScope Completed) Make()
    {
        using var device = new ScopedTargetDevice();
        using var texture = new RenderTexture(2, 2);
        var scope = RenderTextureScope.Push(device, texture);
        var references = new[] { new WeakReference(device), new WeakReference(texture), new WeakReference(device.Current) };
        texture.Release(); scope.Dispose();
        return (references, scope);
    }
}
#endif
sealed class ViewportPlatform : IGraphicsPlatformService
{
    readonly DrawingPlatform _inner = new();
    public bool EmptyViewport { get; set; }
    public GraphicsBackend DefaultBackend => _inner.DefaultBackend;
    public GraphicsBackend PreferredBackend { get; set; }
    public GraphicsDeviceCapabilities? ActiveCapabilities => null;
    public IReadOnlyList<GraphicsBackendDescriptor> Backends => [];
    public void ReportActive(GraphicsDeviceCapabilities c) { }
    public GraphicsShaderPair ResolveShaderVariant(GraphicsDeviceCapabilities c, string stem) => _inner.ResolveShaderVariant(c, stem);
    public GraphicsRect ResolveViewport(GraphicsRect target, Rect normalized, GraphicsBackend backend) => EmptyViewport ? new(0, 0, 0, 0) : target;
}
