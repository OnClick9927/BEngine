using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.UIElements;
using Microsoft.Extensions.DependencyInjection;

DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton(new GraphicsUtility(new DrawingPlatform())).BuildServiceProvider();
PlatformRuntime.Initialize(services);
RuntimeHelpers.RunModuleConstructor(typeof(UIDocument).Module.ModuleHandle);
using var scene = new Scene("Drawing comparison", services);
var document = scene.CreateGameObject("UI").AddComponent<UIDocument>();
var root = document.rootVisualElement; root.style.width = 1280; root.style.height = 720;
for (var index = 0; index < 500; index++)
{
    var element = new VisualElement(); element.style.position = Position.Absolute;
    element.style.left = index % 25 * 45; element.style.top = index / 25 * 30;
    element.style.width = 40; element.style.height = 25;
    element.style.backgroundColor = new UIColor((byte)(index % 255), 90, 200);
    root.Add(element);
}
for (var index = 0; index < 500; index++)
{
    var target = scene.CreateGameObject("Sprite"); target.transform.position = new Vector2(index % 25 / (Fix64)4 - 3, index / 25 / (Fix64)4 - 2);
    var sprite = target.AddComponent<SpriteRenderer>(); sprite.size = new Vector2(Fix64.Half, Fix64.Half);
}
using var device = new RecordingDevice();
using var renderer = new PortableSceneRenderer(device);
for (var index = 0; index < 200; index++) renderer.Render(scene, RenderCamera.Default, 1280, 720);
var samples = new List<object>();
for (var sample = 0; sample < 7; sample++)
{
    var beforeDraws = device.Draws; var beforeVertices = device.Vertices;
    var start = Stopwatch.GetTimestamp(); var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    for (var index = 0; index < 1000; index++) renderer.Render(scene, RenderCamera.Default, 1280, 720);
    var bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    var stats = renderer.LastRenderStatistics;
    if (stats.VisibleSubmissionCount != 1000 || stats.BatchCount != 2 || device.Draws - beforeDraws != 2000) throw new Exception($"Draw output changed: {stats}");
    samples.Add(new { Milliseconds = elapsed, AllocatedBytes = bytes, Draws = device.Draws - beforeDraws, Vertices = device.Vertices - beforeVertices, stats.VisibleSubmissionCount, stats.BatchCount });
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = true, FramesPerSample = 1000, Sprites = 500, UiRectangles = 500, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
