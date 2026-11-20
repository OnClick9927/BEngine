using System.Diagnostics;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform.DotNet;

Directory.CreateDirectory(args[0]); DotNetPlatformServices.Initialize(); AssetPipelineRuntime.Initialize();
using var scene = new Scene("Identical benchmark fixture");
var root = scene.CreateGameObject("Root");
for (var i = 0; i < 80; i++) { var child = scene.CreateGameObject("Object " + i); child.transform.SetParent(root.transform); child.AddComponent<Marker>().Value = i; }
var codec = RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>()!;
var yaml = codec.Serialize(root, null);
var prefab = codec.Deserialize(yaml, "");
using var target = new Scene("Instantiated fixture");
Action parse = () => BObject.DestroyImmediate(codec.Deserialize(yaml, ""));
Action instantiate = () => BObject.DestroyImmediate(prefab.Instantiate(target));
object Measure(Action work, int count)
{
    for (var i = 0; i < 20; i++) work();
    var samples = new List<object>();
    for (var sample = 0; sample < 9; sample++)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var stamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++) work();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        samples.Add(new { Milliseconds = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds, AllocatedBytes = bytes });
    }
    return new { Calls = count, Samples = samples };
}
var result = new { ObjectCount = prefab.objectCount, Parse = Measure(parse, 40), Instantiate = Measure(instantiate, 10) };
File.WriteAllText(Path.Combine(args[0], "Result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
public sealed class Marker : Behaviour { public int Value; }
