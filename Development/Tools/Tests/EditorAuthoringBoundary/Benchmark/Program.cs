using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(Path.GetDirectoryName(output)!, "EditorData"));
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);
using var scene = new Scene("Picking benchmark", services);
for (var i = 0; i < 100; i++)
{
    var renderer = scene.CreateGameObject("Sprite " + i).AddComponent<SpriteRenderer>();
    renderer.gameObject.transform.position = new Vector2(i % 10 - 5, i / 10 - 5);
    renderer.orderInLayer = i;
}
var camera = RenderCamera.Default;
var pickType = Assembly.Load("BEngine.Rendering2D.Editor").GetType("BEngine.Editor.ScenePickingUtility")
    ?? Assembly.Load("BEngine.EditorWorkbench.Editor").GetType("BEngine.Editor.ScenePickingUtility", true)!;
var pick = pickType.GetMethod("PickAll", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
    .CreateDelegate<Func<IReadOnlyList<Scene>, RenderCamera, Vector2, int, int, IReadOnlyList<GameObject>>>();
var draw = typeof(SceneView).GetMethod("DrawTools", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
    .CreateDelegate<Func<Rect, RenderCamera, bool>>();
SceneView.duringSceneGui += OnDraw;
static void OnDraw(SceneViewToolContext context) => context.suppressDefaultTools = true;
Scene[] scenes = [scene]; var view = new Rect(0, 0, 640, 360); var pointer = new Vector2(320, 180);
for (var i = 0; i < 100; i++) { _ = pick(scenes, camera, pointer, 640, 360); _ = draw(view, camera); }
var picking = new List<object>(); var tools = new List<object>(); var hits = 0;
for (var sample = 0; sample < 11; sample++)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
    for (var i = 0; i < 100; i++) hits += pick(scenes, camera, pointer, 640, 360).Count;
    picking.Add(new { Milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds, AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
    allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
    for (var i = 0; i < 10000; i++) if (!draw(view, camera)) throw new Exception("Tool dispatch changed");
    tools.Add(new { Milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds, AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
}
SceneView.duringSceneGui -= OnDraw;
if (hits == 0) throw new Exception("Empty benchmark");
File.WriteAllText(output, JsonSerializer.Serialize(new { SpriteCount = 100, PickingCallsPerSample = 100, ToolCallsPerSample = 10000, Hits = hits, Picking = picking, Tools = tools }, new JsonSerializerOptions { WriteIndented = true }));
