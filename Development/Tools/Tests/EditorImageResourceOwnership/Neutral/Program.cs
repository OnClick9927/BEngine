using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
var owner = typeof(CanvasResourceUtility).Assembly;
Check(owner.GetName().Name == "BEngine.Core.Editor", "Image sources belong to shared editor services");
Check(typeof(IGpuCanvasResourceResolver).Assembly == owner && typeof(FileGpuCanvasResourceResolver).Assembly == owner,
    "The backend contract and file policy share their service owner");
Check(!owner.GetReferencedAssemblies().Any(a => a.Name is "BEngine.IMGUI.Editor" or "BEngine.Graphics" or "BEngine.UIElements"),
    "The service owner has no UI or graphics implementation reference");

DotNetPlatformServices.Initialize();
using var host = new ServiceCollection().AddBEngineDotNetPlatform().BuildServiceProvider();
using var scope = EditorServiceScope.Attach(host);
var defaultUtility = CanvasResourceUtility.Current;
Check(ReferenceEquals(defaultUtility, CanvasResourceUtility.Current), "A host scope reuses its image facade");
Check(ReferenceEquals(EditorServices.GetRequiredService<IGpuCanvasResourceResolver>(), FileGpuCanvasResourceResolver.Shared),
    "Loading EditorServices alone contributes the default backend");

var resources = Path.Combine(output, "Roots");
Directory.CreateDirectory(Path.Combine(resources, "Editor"));
Directory.CreateDirectory(Path.Combine(resources, "Resources"));
File.WriteAllBytes(Path.Combine(resources, "Editor", "icon.png"), [7, 8, 9]);
File.WriteAllBytes(Path.Combine(resources, "Resources", "icon.png"), [0]);
File.WriteAllBytes(Path.Combine(resources, "Resources", "fallback.png"), [7, 8, 9]);
File.WriteAllBytes(Path.Combine(resources, "Resources", "invalid.png"), [0]);
var oldCodec = EncodedImages.Current;
Resources.RegisterResourceRoot(resources);
try
{
    EncodedImages.Current = new ImageCodec();
    var resolver = FileGpuCanvasResourceResolver.Shared;
    Check(resolver.TryResolveTexture("icon.png?bengine-preview=test", out var icon) && icon.Width == 1 && icon.Height == 1,
        "Editor roots win over runtime roots and the revision suffix is stripped");
    Check(icon.Pixels.Span.SequenceEqual(new byte[] { 11, 22, 33, 255 }), "The codec's exact RGBA bytes and alpha are preserved");
    Check(resolver.TryResolveTexture("fallback", out _), "Runtime fallback preserves extension search");
    Check(resolver.TryResolveTexture(Path.Combine(resources, "Editor", "icon.png"), out _), "Absolute source paths still work");
    Check(!resolver.TryResolveTexture("missing.png", out _) && !resolver.TryResolveTexture("invalid.png", out _),
        "Missing and undecodable sources remain unavailable");
    Check(!resolver.TryResolveTexture("../escape.png", out _) && !resolver.TryResolveTexture(" ", out _),
        "Escaping and blank relative sources remain unavailable");
    EncodedImages.Current = null;
    Check(!resolver.TryResolveTexture("icon.png", out _), "An absent codec fails without importing Graphics");
    EncodedImages.Current = new ImageCodec();
    Check(defaultUtility.TryResolveTexture("icon.png", out _), "The raw facade delegates to the file backend");

    var backend = new MemorySource();
    using (var custom = new ServiceCollection().AddSingleton<IGpuCanvasResourceResolver>(backend).BuildServiceProvider())
    using (EditorServiceScope.Attach(custom))
    {
        var facade = CanvasResourceUtility.Current;
        Check(!ReferenceEquals(facade, defaultUtility), "A nested host gets its own facade");
        Check(facade.TryResolvePresentationTexture("memory", out var memory) && memory.Width == 2 && backend.Calls == 1,
            "Explicit host backends are preserved by TryAdd contributions");
        var misses = 0; var wins = 0; var late = 0;
        bool Miss(string source, out EditorTextureData texture) { misses++; texture = default; return false; }
        bool Win(string source, out EditorTextureData texture) { wins++; texture = new(3, 1, GraphicsTextureFormat.R8Unorm, new byte[3]); return true; }
        bool Late(string source, out EditorTextureData texture) { late++; texture = default; return false; }
        EditorInstrumentation.TextureRequested += Miss;
        EditorInstrumentation.TextureRequested += Win;
        EditorInstrumentation.TextureRequested += Late;
        try
        {
            var calls = backend.Calls;
            Check(facade.TryResolvePresentationTexture("override", out var value) && value.Width == 3 &&
                misses == 1 && wins == 1 && late == 0 && backend.Calls == calls,
                "Presentation overrides retain ordering and stop before later handlers and the file backend");
            Check(facade.TryResolveTexture("raw", out var raw) && raw.Width == 2 && wins == 1,
                "The existing raw API does not acquire instrumentation semantics");
        }
        finally
        {
            EditorInstrumentation.TextureRequested -= Miss;
            EditorInstrumentation.TextureRequested -= Win;
            EditorInstrumentation.TextureRequested -= Late;
        }
        Check(facade.TryResolvePresentationTexture("again", out var restored) && restored.Width == 2,
            "Removing overrides immediately restores the current backend");
        _ = Measure(facade);
        var measured = Measure(facade);
        File.WriteAllText(Path.Combine(output, "Performance.json"), JsonSerializer.Serialize(new { Calls = 50000, AllocatedBytes = measured.Bytes, Milliseconds = measured.Milliseconds }));
        Check(measured.Bytes == 0, "50,000 warmed presentation-source calls allocate no managed bytes: " + measured.Bytes);
    }
    Check(ReferenceEquals(EditorServices.GetRequiredService<IGpuCanvasResourceResolver>(), FileGpuCanvasResourceResolver.Shared) &&
        CanvasResourceUtility.Current.TryResolveTexture("icon.png", out _),
        "Disposing a nested scope restores the original host's source backend");
}
finally { EncodedImages.Current = oldCodec; Resources.UnregisterResourceRoot(resources); }

var composition = new ServiceCollection(); RuntimeServiceContributions.Configure(composition);
Check(composition.Any(d => d.ServiceType == typeof(CanvasResourceUtility)) && composition.Any(d => d.ServiceType == typeof(IGpuCanvasResourceResolver)),
    "Shared image services participate in the existing contribution mechanism");
RuntimeServiceContributions.UnregisterAssembly(owner);
var retired = new ServiceCollection(); RuntimeServiceContributions.Configure(retired);
Check(!retired.Any(d => d.ServiceType == typeof(CanvasResourceUtility) || d.ServiceType == typeof(IGpuCanvasResourceResolver)),
    "Retiring the owner removes its registration instead of retaining a UI singleton");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.IMGUI.Editor" or "BEngine.UIElements" or "BEngine.Graphics" or "BEngine.EditorWorkbench.Editor"),
    "Image resolution ran without loading either UI renderer, Graphics or the workbench");
Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a => a.Name is "BEngine.IMGUI.Editor" or "BEngine.UIElements" or "BEngine.Graphics"),
    "The independent consumer has no compile reference to a concrete UI or decoder package");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("NEUTRAL_EDITOR_IMAGE_SOURCES_OK|" + checks.Count);

[MethodImpl(MethodImplOptions.NoInlining)]
static (long Bytes, double Milliseconds) Measure(CanvasResourceUtility facade)
{
    var timer = Stopwatch.StartNew();
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 50000; i++) if (!facade.TryResolvePresentationTexture("warm", out _)) throw new Exception("Source lost");
    var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    timer.Stop(); return (bytes, timer.Elapsed.TotalMilliseconds);
}

sealed class MemorySource : IGpuCanvasResourceResolver
{
    private static readonly byte[] Pixels = [1, 2]; public int Calls;
    public bool TryResolveTexture(string source, out EditorTextureData texture)
    { Calls++; texture = new(2, 1, GraphicsTextureFormat.R8Unorm, Pixels); return true; }
}
sealed class ImageCodec : IEncodedImageCodec
{
    public bool TryGetInfo(byte[] bytes, out int width, out int height) { width = height = 1; return true; }
    public bool TryDecode(ReadOnlySpan<byte> bytes, out int width, out int height, out byte[] rgba)
    { width = height = 1; rgba = [11, 22, 33, 255]; return bytes.SequenceEqual(new byte[] { 7, 8, 9 }); }
}
