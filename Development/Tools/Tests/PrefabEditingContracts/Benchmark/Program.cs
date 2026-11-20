using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); Directory.SetCurrentDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize(); AssetPipelineRuntime.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform, TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
PlayerHostServices.ConfigureDataDirectory(Path.Combine(output, "Assets")); Directory.CreateDirectory(Application.dataPath);
using var sourceScene = new Scene("source"); using var scene = new Scene("live");
var source = sourceScene.CreateGameObject("source"); var sourceValue = source.AddComponent<PrefabProbe>(); sourceValue.Count = 17;
for (var i = 0; i < 8; i++)
{
    var child = sourceScene.CreateGameObject("child" + i); child.transform.SetParent(source.transform, false);
    child.AddComponent<PrefabProbe>().Target = source;
}
var codec = RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>()!;
var prefab = codec.Save(source, Path.Combine(Application.dataPath, "bench.prefab.yaml"), null);
var root = prefab.Instantiate(scene); var value = root.GetComponent<PrefabProbe>()!;
var expectedChildren = root.transform.children.Select(t => t.gameObject).ToArray();
void Run(int iterations)
{
    for (var i = 0; i < iterations; i++)
    {
        value.Count = 99;
        PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
        if (value.Count != 17 || !root.transform.children.Select(t => t.gameObject).SequenceEqual(expectedChildren))
            throw new InvalidOperationException("Benchmark behavior changed");
    }
}
Run(20);
var samples = new List<object>();
for (var sample = 0; sample < 7; sample++)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
    Run(100); watch.Stop();
    samples.Add(new { Milliseconds = watch.Elapsed.TotalMilliseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
}
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Iterations = 100, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PREFAB_BENCHMARK_OK");
