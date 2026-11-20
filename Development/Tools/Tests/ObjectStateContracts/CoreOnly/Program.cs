using System.Text.Json;
using BEngine;
using BEngine.Serialization;
using BEngine.Platform;

var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
PlatformRuntime.Initialize(new HeadlessServices());
Check(TextDataSerialization.Codec is null, "Core-only host has no text-format implementation");
var node = new StateNode { Value = 25 }; node.Next = node;
var source = new StateAsset { Node = node };
var state = ObjectStateSnapshot.Capture(source);
var target = new StateAsset(); state.Restore(target);
Check(target.Node.Value == 25 && ReferenceEquals(target.Node, target.Node.Next), "Object graph capture and restore work without graphics or a text codec");
source.Node.Value = 33; state.Clone().Restore(target);
Check(target.Node.Value == 25, "Detached snapshots remain independent without a format provider");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.Core.Editor"),
    "Headless state execution loads no graphics, scene renderer or Editor assembly");
BObject.DestroyImmediate(source); BObject.DestroyImmediate(target);
Check(source.isDestroyed && target.isDestroyed, "Headless objects retain normal destruction semantics");
Directory.CreateDirectory(args[0]);
File.WriteAllText(Path.Combine(args[0], "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks }));
Console.WriteLine($"CORE_ONLY_STATE_OK|checks={checks.Count}");
public sealed class StateAsset : ScriptableObject { public StateNode Node { get; set; } = new(); }
public sealed class StateNode { public int Value { get; set; } public StateNode? Next { get; set; } }
sealed class HeadlessServices : IServiceProvider
{
    private readonly PlatformUtility _platform = new(new HeadlessPlatform());
    public object? GetService(Type type) => type == typeof(PlatformUtility) ? _platform : null;
}
