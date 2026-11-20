using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform.DotNet;
using BEngine.Serialization;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
DotNetPlatformServices.Initialize();
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var core = typeof(EditorObjectSnapshot).Assembly;
Check(core.GetName().Name == "BEngine.Core.Editor", "Memory snapshots belong to Core Editor");
Check(new[] { typeof(EditorValueCloner), typeof(EditorStateMemoryEstimate), typeof(EditorAssetInfo) }.All(t => t.Assembly == core),
    "Cloning, memory estimates and asset query values share the Core Editor mechanism assembly");
Check(!core.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name),
    "The independent consumer has no private-access grant");
Check(typeof(BObject).Assembly.GetType(typeof(EditorObjectSnapshot).FullName!) is null,
    "Editor snapshot code is absent from Core Runtime");
var assetInfo = new EditorAssetInfo(Guid.NewGuid(), "Assets/a.asset", "source", "artifact", "Data", false, null, 0);
var copyInfo = assetInfo with { AssetPath = "Assets/b.asset" };
Check(assetInfo.AssetPath == "Assets/a.asset" && copyInfo.AssetPath == "Assets/b.asset", "Asset query values have detached copy semantics");
Check(!assetInfo.IsSubAsset && (assetInfo with { ParentGuid = Guid.NewGuid(), LocalIdentifier = 7 }).IsSubAsset,
    "Asset query information preserves main and subasset identity");
Check(typeof(EditorAssetInfo).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).All(f => f.IsInitOnly),
    "Asset query storage is immutable");

var borrowed = new StateAsset { name = "borrowed" };
var node = new StateNode { Value = 4, Borrowed = borrowed }; node.Next = node;
var buffer = new BufferValue(node) { Borrowed = borrowed, Callback = () => { } };
var bufferCopy = (BufferValue)EditorValueCloner.CloneSerializedBufferValue(buffer)!;
Check(!ReferenceEquals(buffer, bufferCopy) && !ReferenceEquals(node, bufferCopy.ReadPrivate()), "Buffer cloning detaches private readonly managed state");
Check(ReferenceEquals(bufferCopy.ReadPrivate(), bufferCopy.ReadPrivate().Next), "Buffer cloning retains managed cycles");
Check(ReferenceEquals(bufferCopy.Borrowed, borrowed) && ReferenceEquals(bufferCopy.ReadPrivate().Borrowed, borrowed),
    "Buffer cloning borrows live engine objects");
Check(buffer.GetterCalls == 0 && bufferCopy.GetterCalls == 0, "Buffer cloning never invokes unrelated property getters");
Check(ReferenceEquals(buffer.Callback, bufferCopy.Callback), "Buffer cloning preserves delegate runtime state");
Check(EditorValueCloner.CloneValue(buffer.Callback) is null, "Mapped serialized copies omit delegates");
var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
var first = EditorValueCloner.CloneSerializedBufferValue(node, visited);
var second = EditorValueCloner.CloneSerializedBufferValue(node, visited);
Check(ReferenceEquals(first, second), "A caller-owned clone context preserves aliases across roots");
Throws<ArgumentNullException>(() => EditorValueCloner.CloneSerializedBufferValue(node, null!), "Null clone context is rejected");
var lower = Array.CreateInstance(typeof(StateNode), [2, 2], [-2, 3]); lower.SetValue(node, -2, 3); lower.SetValue(node, -1, 4);
var lowerCopy = (Array)EditorValueCloner.CloneSerializedBufferValue(lower)!;
Check(lowerCopy.Rank == 2 && lowerCopy.GetLowerBound(0) == -2 && lowerCopy.GetLowerBound(1) == 3, "Multidimensional buffers preserve lower bounds");
Check(ReferenceEquals(lowerCopy.GetValue(-2, 3), lowerCopy.GetValue(-1, 4)) && !ReferenceEquals(node, lowerCopy.GetValue(-2, 3)),
    "Multidimensional buffers preserve aliases without retaining mutable source nodes");
var box = (StateBox)EditorValueCloner.CloneValue(new StateBox(node))!;
Check(!ReferenceEquals(box.Node, node) && ReferenceEquals(box.Node, box.Node.Next), "Value types containing managed references clone their graph");
var mappedObject = new StateAsset { name = "mapped" };
var source = new ReboundSource { Count = 9, Nodes = [node, node], Borrowed = borrowed };
var rebound = (ReboundTarget)EditorValueCloner.CloneValue(source, typeof(ReboundTarget),
    value => ReferenceEquals(value, borrowed) ? mappedObject : value, new(ReferenceEqualityComparer.Instance))!;
Check(rebound.Count == 9 && rebound.Nodes[0].Value == 4, "Typed cloning migrates managed fields to the destination layout");
Check(ReferenceEquals(rebound.Nodes[0], rebound.Nodes[1]) && ReferenceEquals(rebound.Nodes[0], rebound.Nodes[0].Next),
    "Typed cloning preserves aliases and cycles after rebinding types");
Check(ReferenceEquals(rebound.Borrowed, mappedObject) && ReferenceEquals(rebound.Nodes[0].Borrowed, mappedObject),
    "Typed cloning uses the supplied engine-object mapper throughout the graph");
Throws<InvalidDataException>(() => EditorValueCloner.CloneValue(null, typeof(int), value => value, new(ReferenceEqualityComparer.Instance)),
    "Typed cloning rejects null for a non-nullable value type");

var target = new StateAsset { name = "original", Count = 7, First = node, Second = node, Borrowed = borrowed, Items = [node, node] };
var identity = target.Id;
var snapshot = EditorObjectSnapshot.Capture(target);
node.Value = 88; target.Count = 90; target.name = "changed"; target.First = null; target.Items.Clear();
snapshot.Restore();
Check(target.Count == 7 && target.name == "original" && target.Id == identity, "Object snapshots restore values and preserve engine identity");
Check(target.First!.Value == 4 && ReferenceEquals(target.First, target.Second) && ReferenceEquals(target.First, target.Items[0]),
    "Object snapshots detach their graph while preserving shared members");
Check(ReferenceEquals(target.First, target.First.Next) && ReferenceEquals(target.Borrowed, borrowed),
    "Object snapshots preserve cycles and borrowed engine references");
target.First.Value = 50; snapshot.Restore();
Check(target.First!.Value == 4 && !ReferenceEquals(target.First, node), "Repeated restores never mutate the retained snapshot");
var destination = new StateAsset { name = "destination", Count = -1 }; var destinationId = destination.Id;
snapshot.CopyValuesTo(destination);
Check(destination.Count == 7 && destination.name == "original" && destination.Id == destinationId,
    "Copying captured values preserves destination identity");
Check(!ReferenceEquals(destination.First, target.First) && ReferenceEquals(destination.First, destination.Second) && ReferenceEquals(destination.Borrowed, borrowed),
    "Each value copy owns its managed graph and borrows engine objects");
Throws<ArgumentException>(() => snapshot.CopyValuesTo(new GameObject("wrong")), "Value copying rejects different engine object types");
Throws<ArgumentNullException>(() => EditorObjectSnapshot.Capture(null!), "Null snapshot source is rejected");
Throws<ArgumentNullException>(() => snapshot.CopyValuesTo(null!), "Null snapshot destination is rejected");
Check(EditorStateMemoryEstimate.Measure(snapshot) == snapshot.EstimatedBytes && snapshot.EstimatedBytes > 256,
    "Memory budgets account for retained snapshots through nonvirtual Core values");
Check(EditorStateMemoryEstimate.Measure(snapshot, snapshot) == snapshot.EstimatedBytes, "Memory estimates deduplicate shared snapshots");
Check(EditorStateMemoryEstimate.Measure(borrowed) == IntPtr.Size, "Memory estimates borrow engine objects without walking engine internals");
var measured = new BufferValue(node); _ = EditorStateMemoryEstimate.Measure(measured);
Check(measured.GetterCalls == 0, "Memory estimation never invokes user property getters");

using var scene = new Scene("state mechanics");
var a = scene.CreateGameObject("a"); var b = scene.CreateGameObject("b");
var component = a.AddComponent<StateComponent>(); component.Count = 11; component.Borrowed = b;
var members = EditorObjectSnapshot.GetSerializableMembers(component);
var expected = members.Select(m => m.Name).ToArray();
if (members is MemberInfo[] owned && owned.Length > 0) owned[0] = typeof(StateNode).GetField(nameof(StateNode.Value))!;
Check(expected.SequenceEqual(EditorObjectSnapshot.GetSerializableMembers(component).Select(m => m.Name)), "Consumer member lists cannot mutate Core's component reflection cache");
var componentState = EditorObjectSnapshot.Capture(component); component.Count = -1; component.enabled = false;
componentState.Restore();
Check(component.Count == 11 && component.enabled && ReferenceEquals(component.Borrowed, b), "Component snapshots preserve enabled state and scene references");
a.transform.localPosition = new Vector2(3, 5); a.transform.localRotation = 17; a.transform.localScale = new Vector2(2, 4);
var transformState = EditorObjectSnapshot.Capture(a.transform);
a.transform.localPosition = Vector2.zero; a.transform.SetSiblingIndex(1); transformState.Restore();
Check(a.transform.localPosition == new Vector2(3, 5) && a.transform.localRotation == (Fix64)17 && a.transform.localScale == new Vector2(2, 4),
    "Transform snapshots restore local pose");
Check(a.transform.GetSiblingIndex() == 0, "Transform snapshots restore sibling ordering");
var objectState = EditorObjectSnapshot.Capture(a); a.SetActive(false); a.name = "mutated"; objectState.Restore();
Check(a.activeSelf && a.name == "a", "GameObject snapshots restore active state and name");

component.Borrowed = null; // Prefab files cannot contain references outside their hierarchy.
var prefab = PrefabDocumentSnapshot.Capture(a).CreateAsset("memory.prefab");
var instance = PrefabAssetOperations.Instantiate(prefab, scene);
var all = PrefabAssetOperations.Traverse(instance).SelectMany(go => go.components.Cast<BObject>().Prepend(go)).ToArray();
var instanceStates = all.Select(EditorObjectSnapshot.Capture).ToArray();
var originalMap = PrefabDocumentSnapshot.CaptureIdMap(instance);
var originalConnection = PrefabDocumentSnapshot.CaptureConnectionMap(instance);
PrefabAssetOperations.DisconnectHierarchy(instance, true);
foreach (var state in instanceStates) state.Restore();
Check(originalMap.OrderBy(x => x.Key).SequenceEqual(PrefabDocumentSnapshot.CaptureIdMap(instance).OrderBy(x => x.Key)),
    "Editor snapshots restore prefab provenance for objects and components");
Check(originalConnection.OrderBy(x => x.Key).SequenceEqual(PrefabDocumentSnapshot.CaptureConnectionMap(instance).OrderBy(x => x.Key)),
    "Editor snapshots restore the prefab reference identity map");
using var runningScene = new Scene("transient"); var running = runningScene.CreateGameObject("play object");
var runtime = new SceneRuntime(runningScene); runtime.Start();
var runtimeState = EditorObjectSnapshot.Capture(running); running.name = "during play"; runtimeState.Restore();
Check(running.name == "play object", "Editor snapshots support transient Play Mode objects without persistent serialization");
runtime.Stop();
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is
    "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Graphics" or "BEngine.Rendering2D"),
    "Core Editor snapshot tests load no workbench, UI or graphics packages");
var result = new { Passed = true, Checks = checks.Count, Names = checks };
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_CORE_STATE_CONTRACTS_OK " + checks.Count);
