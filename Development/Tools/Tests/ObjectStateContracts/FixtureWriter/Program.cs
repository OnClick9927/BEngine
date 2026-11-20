using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
RuntimeHelpers.RunModuleConstructor(typeof(Document<AnimationClip>).Module.ModuleHandle);
RuntimeHelpers.RunModuleConstructor(typeof(AnimationClip).Module.ModuleHandle);
var clip = new AnimationClip { name = "Legacy motion", frameRate = 30, wrapMode = WrapMode.Loop };
clip.SetCurve("child", typeof(Transform), nameof(Transform.localRotation),
    new AnimationCurve(new Keyframe(0, 12), new Keyframe(2, 90)));
clip.AddEvent(new AnimationEvent { time = Fix64.Half, functionName = "OnFootstep", intParameter = 7 });
clip.Save(Path.Combine(output, "clip.anim.yaml"));
var controller = new AnimatorController
{
    name = "Legacy controller", defaultState = "Idle",
    states = [new AnimatorState { name = "Idle", speed = Fix64.Half,
        transitions = [new AnimatorTransition { destinationState = "Run", hasExitTime = true, exitTime = Fix64.Half }] },
        new AnimatorState { name = "Run", speed = 2 }],
    layers = [new AnimatorControllerLayer { name = "Overlay", defaultWeight = Fix64.Half,
        defaultState = "OverlayIdle", states = [new AnimatorState { name = "OverlayIdle" }] }]
};
controller.Save(Path.Combine(output, "controller.controller.yaml"));
RuntimeHelpers.RunModuleConstructor(typeof(TextureAtlas).Module.ModuleHandle);
var atlas = new TextureAtlas { name = "Legacy atlas", Padding = 3, MaxSize = 512 };
RuntimeAssetReferences.BindSource(atlas, "atlas.yaml", "", "", Guid.Parse("dab153ae-d11d-440e-9232-8ad789d8cb54"), "TextureAtlas");
File.WriteAllText(Path.Combine(output, "atlas.yaml"), YamlUtility.Serialize(atlas));
var source = File.ReadAllBytes(Path.Combine(output, "controller.controller.yaml"));
for (var i = 0; i < 20; i++)
{
    RuntimeAssetDecoding.TryDecodeRegistered(source, "controller.controller.yaml", typeof(AnimatorController), out var warm);
    BObject.DestroyImmediate(warm);
}
var samples = new List<object>();
for (var sample = 0; sample < 7; sample++)
{
    GC.Collect();
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
    for (var i = 0; i < 200; i++)
    {
        RuntimeAssetDecoding.TryDecodeRegistered(source, "controller.controller.yaml", typeof(AnimatorController), out var restored);
        BObject.DestroyImmediate(restored);
    }
    watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    samples.Add(new { Milliseconds = watch.Elapsed.TotalMilliseconds, Bytes = allocated });
}
File.WriteAllText(Path.Combine(output, "Measurements.json"), JsonSerializer.Serialize(new { Samples = samples }));
var plain = new BenchData { Name = "plain data", Values = Enumerable.Range(0, 100).ToArray(),
    Items = Enumerable.Range(0, 20).Select(i => new BenchItem { Number = i, Label = "item " + i }).ToList() };
var packed = BinaryDataSerializer.CompileYaml(Encoding.UTF8.GetBytes(YamlUtility.Serialize(plain)));
for (var i = 0; i < 100; i++) _ = BinaryDataSerializer.Deserialize<BenchData>(packed);
var binarySamples = new List<object>();
for (var sample = 0; sample < 7; sample++)
{
    GC.Collect(); var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
    for (var i = 0; i < 1000; i++) _ = BinaryDataSerializer.Deserialize<BenchData>(packed);
    watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    binarySamples.Add(new { Milliseconds = watch.Elapsed.TotalMilliseconds, Bytes = allocated });
}
File.WriteAllText(Path.Combine(output, "BinaryMeasurements.json"), JsonSerializer.Serialize(new { Samples = binarySamples }));
Console.WriteLine("FIXTURES_AND_MEASUREMENTS_READY");

public sealed class BenchData { public string Name { get; set; } = ""; public int[] Values { get; set; } = []; public List<BenchItem> Items { get; set; } = []; }
public sealed class BenchItem { public int Number { get; set; } public string Label { get; set; } = ""; }
