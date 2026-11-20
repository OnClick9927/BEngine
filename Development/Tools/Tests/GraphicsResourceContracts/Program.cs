using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
T Complete<T>(BTask<T> task)
{
    var clock = Stopwatch.StartNew();
    while (!task.IsCompleted && clock.Elapsed.TotalSeconds < 30)
    { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(1)); Thread.Yield(); }
    if (!task.IsCompleted) throw new TimeoutException("Resource preparation timed out.");
    return task.GetAwaiter().GetResult();
}
DotNetPlatformServices.Initialize();
RuntimeHelpers.RunModuleConstructor(typeof(Document<Material>).Module.ModuleHandle);
RuntimeHelpers.RunModuleConstructor(typeof(Texture).Module.ModuleHandle);
Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
    .Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name), "Probe has no private-access grant");
Check(!typeof(Texture).Assembly.GetReferencedAssemblies().Any(a => a.Name is "BEngine.Rendering2D" or "BEngine.UIElements"), "Graphics resources are independent of scene and UI renderers");
Check(typeof(BObject).Assembly.GetType("BEngine.Texture") is null, "Core does not own texture implementation");
var original = RuntimeServiceContributions.GetDefault<IAssetReferenceResolver>();
var resolver = new ProbeResolver(original);
RuntimeServiceContributions.RegisterDefault<IAssetReferenceResolver>(resolver);
Check(RuntimeAssetReferences.TryReadMetadata("probe", out var meta) && ReferenceEquals(meta, resolver.Metadata), "Source metadata uses the registered resolver");
Throws<ArgumentException>(() => Resources.LoadAsync(" ", typeof(byte[]), new()), "Blank async address is rejected");
Throws<ArgumentNullException>(() => Resources.LoadAsync("probe.png", null!, new()), "Null async type is rejected");
Throws<ArgumentNullException>(() => Resources.LoadAsync("probe.png", typeof(byte[]), null!), "Null preparation context is rejected");
using var image = Texture.FromRgba32(2, 2, [255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255]).AsOwned();
var png = image.Value.EncodeToPNG();
var imagePath = Path.Combine(output, "image.png"); File.WriteAllBytes(imagePath, png);
var sourceTexture = (Texture)RuntimeAssetDecoding.LoadFile(imagePath, typeof(Texture))!;
Check(sourceTexture.width == 2 && sourceTexture.isReadable && sourceTexture.filterMode == TextureFilterMode.Point && sourceTexture.wrapMode == TextureWrapMode.Repeat,
    "Physical texture loading respects the registered source metadata resolver");
BObject.DestroyImmediate(sourceTexture);
var metadata = new RuntimeResourceMetadata("probe.png", "", "probe.png", Guid.NewGuid(), Guid.Empty, 0, "Texture", "TextureImporter", resolver.Metadata.Settings);
var decoded = (Texture)RuntimeResourceCodecs.Find(typeof(Texture))!.Decode(png, metadata);
RuntimeAssetReferences.Bind(decoded, metadata);
Check(decoded.width == 2 && decoded.height == 2 && decoded.isReadable && decoded.filterMode == TextureFilterMode.Point, "Texture codec preserves dimensions and import settings");
var provider = new ProbeProvider(png, decoded);
Directory.CreateDirectory(Path.Combine(output, "Resources"));
File.WriteAllBytes(Path.Combine(output, "Resources/physical.png"), png);
Resources.RegisterResourceRoot(output);
Resources.RegisterResourceProvider(provider);
try
{
    var progress = 0f;
    using var cancellation = new BCancellationSource();
    var context = new AsyncLoadContext(cancellation.Token, value => progress = value) { priority = 7 };
    var loaded = Complete(Resources.LoadAsync("probe.png", typeof(byte[]), context));
    Check(loaded is byte[] bytes && bytes.SequenceEqual(png), "Caller-context load returns provider bytes");
    Check(ReferenceEquals(provider.Context, context) && provider.Context.priority == 7 && provider.Context.cancellationToken == cancellation.Token,
        "Provider receives the original context, priority and cancellation");
    Check(progress == .5f, "Provider reports through the caller progress sink");
    Throws<ArgumentException>(() => Complete(Resources.LoadAsync(imagePath, typeof(byte[]), new())), "Absolute resource paths remain rejected");
    Check(Complete(Resources.LoadAsync("physical.png", typeof(byte[]), context)) is byte[] physical && physical.SequenceEqual(png),
        "Unhandled resources fall back to physical bytes within the same context");
    provider.CancelDuringLoad = cancellation;
    Throws<BOperationCanceledException>(() => Complete(Resources.LoadAsync("probe.png", typeof(byte[]), context)), "Cancellation after provider completion prevents publication");
    provider.CancelDuringLoad = null;
    var calls = provider.AsyncCalls;
    Throws<BOperationCanceledException>(() => Complete(Resources.LoadAsync("probe.png", typeof(byte[]), context)), "Already cancelled load is rejected");
    Check(provider.AsyncCalls == calls, "Cancelled load never calls the provider");
    provider.WrongType = true;
    Throws<InvalidDataException>(() => Complete(Resources.LoadAsync("probe.png", typeof(byte[]), new())), "Incorrect provider type is rejected");
    provider.WrongType = false;
    provider.Failure = new IOException("fixture failure");
    Throws<IOException>(() => Complete(Resources.LoadAsync("probe.png", typeof(byte[]), new())), "Provider failures are preserved");
    provider.Failure = null;
    var loadedPixels = decoded.GetPixels();
    Check(loadedPixels.Length == 4 && loadedPixels[0].r == Fix64.One && loadedPixels[1].g == Fix64.One, "Readable bundled texture decodes provider pixels");
    var owner = Guid.NewGuid();
    RuntimeAssetReferences.BindSubAsset(decoded, "probe.png", owner, 7001, Guid.NewGuid());
    resolver.Texture = decoded;
    using var material = new Material().AsOwned();
    material.Value.SetTexture("_MainTex", decoded);
    var materialPath = Path.Combine(output, "material.mat");
    material.Value.Save(materialPath);
    Check(File.ReadAllText(materialPath).Contains($"guid:{owner:N}#subasset=7001"), "Material saves stable child identity through the public contract");
    var restored = Material.Load(materialPath);
    Check(ReferenceEquals(restored.GetTexture("_MainTex"), decoded) && resolver.LastLoaded == $"guid:{owner:N}#subasset=7001", "Material restores a child texture through the active asset resolver");
    BObject.DestroyImmediate(restored);
    using var device = new RecordingDevice();
    using var cache = new SceneTextureCache(device);
    Check(Complete(cache.PrepareAsync("probe.png", new())), "GPU preparation loads a logical image through public resources");
    var gpu = cache.Resolve("probe.png");
    Check(gpu.Description.Width == 2 && gpu.Description.Height == 2, "Prepared logical image is resident at the expected dimensions");
    Check(Complete(SceneTextureCache.WaitForReadyAsync(["probe.png"])), "Ready observer accepts the committed image");
    resolver.Advance();
    Check(!Complete(SceneTextureCache.WaitForReadyAsync(["probe.png"])), "Resolver generation invalidates observed GPU residency");
    Check(Complete(cache.PrepareAsync("probe.png", new())), "Image can be prepared again after invalidation");
    cache.BeginFrame(); _ = cache.Resolve("probe.png");
    void WarmFrames() { for (var i = 0; i < 10000; i++) { cache.BeginFrame(); _ = cache.Resolve("probe.png"); } }
    WarmFrames(); WarmFrames();
    var watch = new Stopwatch(); var before = GC.GetAllocatedBytesForCurrentThread();
    watch.Start(); WarmFrames(); watch.Stop(); var hotBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(hotBytes == 0, "10000 warm frame scopes and texture hits allocate zero bytes");
    File.WriteAllText(Path.Combine(output, "Measurements.json"), JsonSerializer.Serialize(new { Calls = 10000, Bytes = hotBytes, Milliseconds = watch.Elapsed.TotalMilliseconds }));
    var target = new RenderTexture(2, 2); var notifications = 0;
    target.destroyed += _ => notifications++;
    var targetBinding = RenderTextureScope.Push(device, target);
    var disposedTargets = device.ResourcesDisposed; var unloads = resolver.UnloadCalls;
    target.Dispose(); target.Dispose();
    Check(device.ResourcesDisposed == disposedTargets && RenderTexture.active is null, "Destroying a bound target retires it until unbinding");
    targetBinding.Dispose();
    Check(target.isDestroyed && notifications == 1 && BObject.FindObjectFromInstanceID(target.GetInstanceID()) is null, "RenderTexture disposal unregisters once through the public lifecycle");
    Check(resolver.UnloadCalls == unloads + 1, "Render target disposal unloads its asset identity exactly once");
    Check(device.ResourcesDisposed == disposedTargets + 1 && RenderTexture.active is null, "Disposal releases the RHI target once and clears the active target");
    Throws<ObjectDisposedException>(() => target.Create(), "Disposed render target cannot be recreated");
    using var runtimeOnly = new Material().AsOwned(); runtimeOnly.Value.SetTexture("_MainTex", image.Value);
    Throws<InvalidOperationException>(() => runtimeOnly.Value.Save(Path.Combine(output, "runtime.mat")), "Runtime-only textures remain unsavable in materials");
    foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32, new UTF32Encoding(true, true) })
    {
        const string text = "// BEngine \u6d4b\u8bd5";
        var payload = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        var shaderMeta = metadata with { Address = "shader.shader", OwnerAddress = "shader.shader", AssetType = "Shader" };
        var shader = (Shader)RuntimeResourceCodecs.Find(typeof(Shader))!.Decode(payload, shaderMeta);
        Check(shader.sourceCode == text, "Shader resource preserves BOM decoding: " + encoding.WebName);
        BObject.DestroyImmediate(shader);
        const string yaml = "format: BEngine.Material\nversion: 2\nname: BomMaterial\nshader: BEngine/Sprite\nrenderQueue: 3142\n";
        var materialMeta = metadata with { Address = "bom.mat", OwnerAddress = "bom.mat", AssetType = "Material" };
        var bomMaterial = (Material)RuntimeResourceCodecs.Find(typeof(Material))!.Decode(encoding.GetPreamble().Concat(encoding.GetBytes(yaml)).ToArray(), materialMeta);
        Check(bomMaterial.name == "BomMaterial" && bomMaterial.renderQueue == 3142, "Material resource preserves BOM decoding: " + encoding.WebName);
        BObject.DestroyImmediate(bomMaterial);
    }
}
finally
{
    Resources.UnregisterResourceProvider(provider);
    Resources.UnregisterResourceRoot(output);
    RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
    BObject.DestroyImmediate(decoded);
}
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("GRAPHICS_RESOURCE_CONTRACTS_OK|" + checks.Count);

static class Ownership
{
    public static Owned<T> AsOwned<T>(this T value) where T : BObject => new(value);
}
sealed class Owned<T>(T value) : IDisposable where T : BObject
{ public T Value => value; public void Dispose() => BObject.DestroyImmediate(value); }
sealed class ProbeProvider(byte[] image, Texture texture) : IAsyncResourceProvider, IResourceAssetProvider
{
    public AsyncLoadContext? Context; public int AsyncCalls; public bool WrongType;
    public BCancellationSource? CancelDuringLoad; public Exception? Failure;
    public bool TryLoad(string path, string folder, out ResourceContent content)
    { content = new(path, image); return path == "probe.png"; }
    public IEnumerable<string> Enumerate(string path, string folder) => [];
    public bool TryLoadAsset(string path, string folder, Type type, out BAsset asset)
    { asset = texture; return path == "probe.png" && type == typeof(Texture); }
    public BTask<ResourceLoadResult> LoadAsync(string path, string folder, Type type, AsyncLoadContext context)
    {
        Context = context; AsyncCalls++; context.ReportProgress(.5f);
        if (path != "probe.png") return BTask<ResourceLoadResult>.FromResult(new(false, null));
        if (Failure is not null) throw Failure;
        CancelDuringLoad?.Cancel();
        return BTask<ResourceLoadResult>.FromResult(new(true, WrongType ? "wrong type" : image));
    }
}
sealed class ProbeResolver(IAssetReferenceResolver? original) : IAssetReferenceResolver
{
    private long _generation;
    public Texture? Texture; public string? LastLoaded; public int UnloadCalls;
    public AssetReferenceMetadata Metadata { get; } = new() { Settings = new() { ["isReadable"] = "true", ["filterMode"] = "Point", ["wrapMode"] = "Repeat" } };
    public long Generation => (original?.Generation ?? 0) + _generation;
    public void Advance() => _generation++;
    public BAsset? Load(string path, Type type) { LastLoaded = path; return Texture is not null && type == typeof(Texture) && path.StartsWith("guid:") ? Texture : original?.Load(path, type); }
    public bool TryReadMetadata(string path, out AssetReferenceMetadata metadata) { metadata = Metadata; return true; }
    public Guid ResolveIdentity(string path, Type type) => original?.ResolveIdentity(path, type) ?? Guid.NewGuid();
    public bool CanPrepareAsync(Type type) => original?.CanPrepareAsync(type) == true;
    public BTask<BAsset?> LoadPreparedAsync(string path, Type type, AsyncLoadContext context) => original?.LoadPreparedAsync(path, type, context) ?? BTask<BAsset?>.FromResult(null);
    public BAsset? LoadByGuid(string guid, Type type) => original?.LoadByGuid(guid, type);
    public BAsset? LoadSubAsset(string path, long id, Type type) => original?.LoadSubAsset(path, id, type);
    public void Invalidate(string path) => original?.Invalidate(path);
    public void Unload(BObject value) { UnloadCalls++; original?.Unload(value); }
    public void PruneMissingFiles() => original?.PruneMissingFiles();
    public void Clear() => original?.Clear();
    public string? ResolveGuidPath(Guid guid) => original?.ResolveGuidPath(guid);
    public AssetReferenceMetadata? ReadSubAssetMetadata(string path) => original?.ReadSubAssetMetadata(path);
}
