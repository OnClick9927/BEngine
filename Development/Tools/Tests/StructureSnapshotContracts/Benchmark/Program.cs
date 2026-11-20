using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform, TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
using var scene = new Scene("benchmark");
var root = scene.CreateGameObject("root");
var children = Enumerable.Range(0, 8).Select(i =>
{
    var child = scene.CreateGameObject("child " + i); child.transform.SetParent(root.transform);
    var value = child.AddComponent<StructureValue>(); value.Count = i; value.Borrowed = root;
    return value;
}).ToArray();
void Round()
{
    Undo.RegisterFullObjectHierarchyUndo(root, "hierarchy");
    children[0].Count = 91; children[0].transform.SetSiblingIndex(7);
    Undo.PerformUndo();
    if (children[0].Count != 0 || children[0].transform.GetSiblingIndex() != 0) throw new Exception("Hierarchy Undo regression");
    Undo.PerformRedo();
    if (children[0].Count != 91 || children[0].transform.GetSiblingIndex() != 7) throw new Exception("Hierarchy Redo regression");
    Undo.ClearAll(); children[0].Count = 0; children[0].transform.SetSiblingIndex(0);
    Undo.DestroyObjectImmediate(children[1]); Undo.PerformUndo(); Undo.PerformRedo(); Undo.PerformUndo();
    if (!ReferenceEquals(children[1].gameObject.GetComponent<StructureValue>(), children[1])) throw new Exception("Structural identity regression");
    Undo.ClearAll();
}
for (var i = 0; i < 500; i++) Round();
var samples = new List<object>();
for (var sample = 0; sample < 7; sample++)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var bytes = GC.GetAllocatedBytesForCurrentThread(); var stamp = Stopwatch.GetTimestamp();
    for (var i = 0; i < 500; i++) Round();
    var milliseconds = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
    samples.Add(new { Milliseconds = milliseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - bytes });
}
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Iterations = 500, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_STRUCTURE_BENCHMARK_OK");
