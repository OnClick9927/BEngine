using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton(new GraphicsUtility(new DrawingPlatform())).BuildServiceProvider();
PlatformRuntime.Initialize(services);
using var scene = new Scene("Vertex upload parity", services);
var texture = Texture.FromRgba32(2, 2, new byte[] { 255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255 });
var sprite = Sprite.Create(texture, new Vector2(Fix64.Half, Fix64.Half));
var sprites = new SpriteRenderer[500];
for (var index = 0; index < sprites.Length; index++)
{
    var target = scene.CreateGameObject("Sprite"); target.transform.position = new Vector2(index % 25 / (Fix64)4 - 3, index / 25 / (Fix64)4 - 2);
    var render = target.AddComponent<SpriteRenderer>(); render.size = new(Fix64.Half, Fix64.Half);
    if (index >= 250) render.sprite = sprite;
    sprites[index] = render;
}
using var device = new RecordingDevice();
using var renderer = new PortableSceneRenderer(device);
using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
device.Capture = hash;
Span<int> counts = stackalloc int[3];
for (var frame = 0; frame < 256; frame++)
{
    var phase = frame / 32;
    var camera = RenderCamera.Default;
    if (phase is 1 or 5) camera = camera with { Position = new Vector2((frame % 32) / (Fix64)128, 0), Rotation = phase == 5 ? (Fix64)(frame % 8) : 0 };
    if (phase == 3) { sprites[0].transform.position = new Vector2((frame % 16) / (Fix64)8, 0); sprites[250].flipX = frame % 2 == 0; sprites[250].flipY = frame % 3 == 0; }
    if (frame == 128) foreach (var item in sprites) item.enabled = false;
    if (frame == 160) foreach (var item in sprites) item.enabled = true;
    if (frame == 192) renderer.geometryCacheBudgetBytes = 0;
    if (frame >= 224) renderer.ClearGeometryCache();
    renderer.Render(scene, camera, frame % 16 < 8 ? 1280 : 960, 720);
    var statistics = renderer.LastRenderStatistics;
    counts[0] = frame; counts[1] = statistics.VisibleSubmissionCount; counts[2] = statistics.BatchCount;
    hash.AppendData(MemoryMarshal.AsBytes(counts));
}
var vertexHash = Convert.ToHexString(hash.GetHashAndReset()); device.Capture = null;
renderer.geometryCacheBudgetBytes = 8 * 1024 * 1024;
sprites[0].transform.position = new(-3, -2); sprites[250].flipX = sprites[250].flipY = false;
var cameras = Enumerable.Range(0, 64).Select(i => RenderCamera.Default with { Position = new Vector2(i / (Fix64)1024, 0) }).ToArray();
var samples = new List<object>();
for (var mode = 0; mode < 2; mode++)
{
    for (var frame = 0; frame < 1000; frame++) renderer.Render(scene, cameras[mode == 0 ? 0 : frame % 64], 1280, 720);
    for (var sample = 0; sample < 5; sample++)
    {
        var draws = device.Draws; var start = Stopwatch.GetTimestamp(); var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 1000; frame++) renderer.Render(scene, cameras[mode == 0 ? 0 : frame % 64], 1280, 720);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated; var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var stats = renderer.LastRenderStatistics;
        if (stats.VisibleSubmissionCount != 500 || device.Draws - draws != 2000 || bytes != 0) throw new Exception($"Unexpected mesh workload: {stats}, {bytes} B, {device.Draws-draws} draws");
        samples.Add(new { Mode = mode == 0 ? "Static" : "Moving", MillisecondsPerFrame = ms / 1000, AllocatedBytes = bytes, Draws = device.Draws - draws });
    }
}
var retainedVertices = new Dictionary<string,long>();
foreach (var field in new[] { "_coreTriangleVertices", "_texturedVertices" })
{
    var buffer = typeof(PortableSceneRenderer).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
    retainedVertices[field] = buffer is List<float> old ? (long)old.Capacity * 4 : (long)buffer.GetType().GetProperty("retainedVertexBytes")!.GetValue(buffer)!;
}
File.WriteAllText(Path.Combine(output,"Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = 4, Frames = 256, VertexAndDrawSha256 = vertexHash, RetainedVertexBytes = retainedVertices, Samples = samples,
    Scope = "500 mixed solid/textured sprites; static/moving projections, partial edits, empty frames, viewport changes and disabled geometry reuse. Recording RHI CPU, not GPU FPS." }, new JsonSerializerOptions { WriteIndented = true }));
BObject.DestroyImmediate(sprite); BObject.DestroyImmediate(texture);
Console.WriteLine("VERTEX_UPLOAD_PARITY_OK");
