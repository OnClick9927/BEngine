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
var node = new StateNode { Value = 4 }; node.Next = node;
var asset = new StateAsset { Count = 7, First = node, Second = node, Items = [node, node] };
void Round()
{
    Undo.RecordObject(asset, "edit"); asset.Count = 23;
    Undo.PerformUndo(); if (asset.Count != 7 || !ReferenceEquals(asset.First, asset.Second) || !ReferenceEquals(asset.First, asset.First!.Next)) throw new Exception("Undo regression");
    Undo.PerformRedo(); if (asset.Count != 23) throw new Exception("Redo regression");
    Undo.ClearAll(); asset.Count = 7;
}
for (var i = 0; i < 1000; i++) Round();
var samples = new List<object>();
for (var sample = 0; sample < 7; sample++)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var bytes = GC.GetAllocatedBytesForCurrentThread(); var stamp = Stopwatch.GetTimestamp();
    for (var i = 0; i < 1000; i++) Round();
    var milliseconds = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
    samples.Add(new { Milliseconds = milliseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - bytes });
}
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Iterations = 1000, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_STATE_BENCHMARK_OK");
