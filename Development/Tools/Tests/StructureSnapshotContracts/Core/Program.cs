using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform.DotNet;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize();
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
Check(typeof(EditorHierarchySnapshot).Assembly == typeof(EditorObjectSnapshot).Assembly &&
      typeof(EditorObjectStructureSnapshot).Assembly == typeof(EditorObjectSnapshot).Assembly,
    "Structural mechanisms belong to Core Editor");
Check(!typeof(EditorHierarchySnapshot).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
    .Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name),
    "The consumer has no Core friendship grant");
Check(typeof(BObject).Assembly.GetType(typeof(EditorHierarchySnapshot).FullName!) is null,
    "Structural authoring code does not enter Core Runtime");
Throws<ArgumentNullException>(() => EditorObjectStructureSnapshot.Capture(null!), "Null structural targets are rejected");
Throws<ArgumentNullException>(() => EditorHierarchySnapshot.Capture(null!), "Null hierarchy roots are rejected");
using var scene = new Scene("core structure");
using var other = new Scene("other scene");
var owner = scene.CreateGameObject("components");
var dependent = owner.AddComponent<StructureDependent>();
var required = owner.GetComponent<StructureRequired>()!;
var value = owner.AddComponent<StructureValue>(); value.Count = 7; value.Borrowed = owner;
Check(!EditorObjectStructureSnapshot.CanRemove(null), "Null targets are not structurally removable");
Check(!EditorObjectStructureSnapshot.CanRemove(new StructureAsset()), "Asset deletion is outside the structural removal query");
Check(!EditorObjectStructureSnapshot.CanRemove(new StructureValue()), "Unattached components can be queried without an owner exception");
Check(!EditorObjectStructureSnapshot.CanRemove(owner.transform), "The structural query protects Transform");
Check(EditorObjectStructureSnapshot.CanRemove(owner), "Attached hierarchy roots can be removed");
Check(!EditorObjectStructureSnapshot.CanRemove(required), "The structural query protects a required component");
Check(EditorObjectStructureSnapshot.CanRemove(dependent) && EditorObjectStructureSnapshot.CanRemove(value),
    "Dependent and unrelated components remain removable");
value.enabled = false;
Check(EditorObjectStructureSnapshot.CanRemove(value), "Disabled components retain structural editability");
value.enabled = true;
var replacement = owner.AddComponent<StructureRequired>();
Check(EditorObjectStructureSnapshot.CanRemove(required) && EditorObjectStructureSnapshot.CanRemove(replacement),
    "Either equivalent required component can be removed while the other satisfies the requirement");
Check(owner.RemoveComponent(replacement) && !EditorObjectStructureSnapshot.CanRemove(required),
    "The query observes changed requirements without stale cached answers");
Check(!EditorObjectStructureSnapshot.CanRemove(replacement), "Removed components are no longer removable");
var queryOrder = owner.components.ToArray();
var queryVersion = scene.hierarchyLookupVersion;
_ = QueryMeasurement.Measure(value, required);
var (queryAllocatedBytes, queryMilliseconds) = QueryMeasurement.Measure(value, required);
Check(queryAllocatedBytes == 0, "Warmed structural removal queries allocate no managed memory (bytes: " + queryAllocatedBytes + ")");
Check(owner.components.SequenceEqual(queryOrder) && scene.hierarchyLookupVersion == queryVersion && value.Count == 7 &&
      ReferenceEquals(BObject.FindObjectFromInstanceID(value.GetInstanceID()), value),
    "Repeated queries preserve component order, hierarchy version, values and registration");
var protectedState = EditorObjectStructureSnapshot.Capture(required);
Check(!protectedState.TryRemove(out var protectedScene) && ReferenceEquals(protectedScene, scene),
    "Removing a required component is refused and reports the affected scene");
Check(ReferenceEquals(owner.GetComponent<StructureRequired>(), required), "A refused removal keeps component identity");
var componentOrder = owner.components.ToArray();
var componentState = EditorObjectStructureSnapshot.Capture(value);
var componentId = value.GetInstanceID();
Check(componentState.TryRemove(out var affected) && ReferenceEquals(affected, scene), "Component removal reports its actual scene");
Check(owner.GetComponent<StructureValue>() is null && BObject.FindObjectFromInstanceID(componentId) is null,
    "Removed components leave owner queries and the live-object registry");
Check(!EditorObjectStructureSnapshot.CanRemove(value), "A removed component cannot be offered for removal again");
value.Count = 12;
componentState.Restore();
Check(ReferenceEquals(owner.GetComponent<StructureValue>(), value) &&
      ReferenceEquals(BObject.FindObjectFromInstanceID(componentId), value), "Component restoration preserves live identity and registry membership");
Check(EditorObjectStructureSnapshot.CanRemove(value), "Undo restoration makes the original component removable again");
Check(owner.components.SequenceEqual(componentOrder), "Component restoration preserves its original index");
Check(value.Count == 12 && ReferenceEquals(value.Borrowed, owner), "Structural restoration does not replace live component values");
componentState.Restore();
Check(owner.components.SequenceEqual(componentOrder), "Repeated component restoration introduces no duplicates");
Check(!EditorObjectStructureSnapshot.Capture(owner.transform).TryRemove(out _), "A Transform cannot be removed independently");
Check(!EditorObjectStructureSnapshot.Capture(new StructureAsset()).TryRemove(out var assetScene) && assetScene is null,
    "Non-structural assets cannot be removed through a structural snapshot");

var parent = scene.CreateGameObject("parent");
var first = scene.CreateGameObject("first"); first.transform.SetParent(parent.transform);
var root = scene.CreateGameObject("root"); root.transform.SetParent(parent.transform);
var last = scene.CreateGameObject("last"); last.transform.SetParent(parent.transform);
var child = scene.CreateGameObject("child"); child.transform.SetParent(root.transform);
var leaf = scene.CreateGameObject("leaf"); leaf.transform.SetParent(child.transform);
var childValue = child.AddComponent<StructureValue>(); childValue.Count = 5; childValue.Borrowed = leaf;
var rootId = root.GetInstanceID(); var leafId = leaf.GetInstanceID();
var structure = EditorObjectStructureSnapshot.Capture(root);
Check(structure.TryRemove(out affected) && ReferenceEquals(affected, scene), "Hierarchy removal reports its scene");
Check(root.scene is null && child.scene is null && leaf.scene is null && root.isDestroyed && leaf.isDestroyed,
    "Hierarchy removal detaches every descendant");
structure.Restore();
Check(ReferenceEquals(root.scene, scene) && ReferenceEquals(child.scene, scene) && ReferenceEquals(leaf.scene, scene),
    "Hierarchy restoration reattaches all descendants");
Check(ReferenceEquals(root.transform.parent, parent.transform) && ReferenceEquals(child.transform.parent, root.transform) &&
      ReferenceEquals(leaf.transform.parent, child.transform), "Hierarchy restoration preserves the parent graph");
Check(parent.transform.children.Select(t => t.gameObject).SequenceEqual(new[] { first, root, last }),
    "Hierarchy restoration preserves sibling order");
Check(ReferenceEquals(BObject.FindObjectFromInstanceID(rootId), root) && ReferenceEquals(BObject.FindObjectFromInstanceID(leafId), leaf),
    "Hierarchy restoration reuses object instances rather than deserializing replacements");
Check(ReferenceEquals(child.GetComponent<StructureValue>(), childValue) && ReferenceEquals(childValue.Borrowed, leaf),
    "Hierarchy restoration preserves component instances and borrowed references");
structure.Restore();
Check(scene.gameObjects.Count(g => ReferenceEquals(g, root)) == 1 && parent.transform.childCount == 3,
    "Repeated hierarchy restoration does not duplicate nodes");

var hierarchy = EditorHierarchySnapshot.Capture(root);
var targets = hierarchy.GetTargets();
Check(targets.Contains(root) && targets.Contains(childValue) && targets.Distinct().Count() == targets.Length,
    "Hierarchy targets include each original object and component exactly once");
targets[0] = null!;
Check(hierarchy.GetTargets().All(t => t is not null), "Callers cannot mutate the retained target list");
root.name = "edited root"; childValue.Count = 41; childValue.enabled = false;
child.transform.SetParent(null, false); Scene.MoveGameObjectToScene(child, other);
var extra = scene.CreateGameObject("extra"); extra.transform.SetParent(root.transform);
var extraLeaf = scene.CreateGameObject("extra leaf"); extraLeaf.transform.SetParent(extra.transform);
var extraValue = extra.AddComponent<StructureValue>(); extraValue.Count = 21;
var requiredOnRoot = root.AddComponent<StructureRequired>();
var dependentOnRoot = root.AddComponent<StructureDependent>();
Check(!hierarchy.GetTargets().Contains(extra) && !hierarchy.GetTargets().Contains(dependentOnRoot),
    "Captured targets do not drift when the live hierarchy changes");
var inverse = hierarchy.CaptureInverse();
hierarchy.Restore();
Check(root.name == "root" && childValue.Count == 5 && childValue.enabled, "Hierarchy values and enabled state are restored");
Check(ReferenceEquals(child.scene, scene) && ReferenceEquals(leaf.scene, scene) && ReferenceEquals(child.transform.parent, root.transform),
    "Cross-scene descendants return to their original scene and parent");
Check(extra.scene is null && extraLeaf.scene is null && extra.isDestroyed, "Objects introduced after capture are removed as a subtree");
Check(root.GetComponent<StructureRequired>() is null && root.GetComponent<StructureDependent>() is null,
    "New components are removed in dependency-safe order");
inverse.Restore();
Check(root.name == "edited root" && childValue.Count == 41 && !childValue.enabled, "Inverse capture retains the changed values");
Check(ReferenceEquals(child.scene, other) && child.transform.parent is null && ReferenceEquals(leaf.scene, other),
    "Inverse restoration restores the cross-scene move");
Check(ReferenceEquals(extra.scene, scene) && ReferenceEquals(extraLeaf.transform.parent, extra.transform) &&
      ReferenceEquals(extra.GetComponent<StructureValue>(), extraValue), "Inverse restoration resurrects added nodes with the same identities");
Check(ReferenceEquals(root.GetComponent<StructureRequired>(), requiredOnRoot) && ReferenceEquals(root.GetComponent<StructureDependent>(), dependentOnRoot),
    "Inverse restoration preserves required/dependent component instances");
for (var i = 0; i < 3; i++) { hierarchy.Restore(); inverse.Restore(); }
Check(scene.gameObjects.Count(g => ReferenceEquals(g, extra)) == 1 && root.components.Count == 3 && ReferenceEquals(child.scene, other),
    "Repeated inverse cycles preserve membership and component counts");

var deleted = EditorHierarchySnapshot.Capture(extra);
scene.Destroy(extra);
var deletedInverse = deleted.CaptureInverse();
deleted.Restore();
Check(ReferenceEquals(extra.scene, scene) && ReferenceEquals(extraLeaf.scene, scene), "A deleted hierarchy can be restored in place");
deletedInverse.Restore();
Check(extra.scene is null && extraLeaf.scene is null, "The inverse of a deletion removes the resurrected hierarchy");
var detached = new GameObject("detached");
Check(!EditorObjectStructureSnapshot.CanRemove(detached), "A detached hierarchy cannot be deleted from an invented scene");
var detachedComponent = detached.AddComponent<StructureValue>();
Check(EditorObjectStructureSnapshot.CanRemove(detachedComponent), "A component can be edited on a detached but live owner");
var detachedState = EditorObjectStructureSnapshot.Capture(detached);
Check(!detachedState.TryRemove(out var detachedScene) && detachedScene is null, "Detached objects are not assigned an invented scene");
var detachedHierarchy = EditorHierarchySnapshot.Capture(detached);
detached.name = "changed"; detachedHierarchy.Restore();
Check(detached.scene is null && detached.name == "detached", "Detached hierarchy values restore without a scene or window");
Throws<ArgumentException>(() => new FactorySnapshot(parent, hierarchy), "Snapshots from different roots cannot share reconciliation state");
FactorySnapshot.Captures = 0;
_ = new FactorySnapshot(owner, null);
Check(FactorySnapshot.Captures == owner.components.Count + 1, "The workflow can supply its own value-capture policy through the protected extension contract");
var transientScene = new Scene("disposed");
var transientRoot = transientScene.CreateGameObject("transient");
var transientState = EditorObjectStructureSnapshot.Capture(transientRoot); transientScene.Dispose();
Check(!EditorObjectStructureSnapshot.CanRemove(transientRoot) && !EditorObjectStructureSnapshot.CanRemove(transientRoot.transform),
    "Disposed hierarchy targets are not offered as editable structure");
Throws<ObjectDisposedException>(transientState.Restore, "Restoration refuses a disposed scene");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is
    "BEngine.Core.Editor" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Graphics" or "BEngine.Rendering2D"),
    "Structural contracts run without editor workflow, GUI, graphics or native windows");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks,
    QueryMeasurement = new { Calls = 200000, Milliseconds = queryMilliseconds, AllocatedBytes = queryAllocatedBytes } }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_STRUCTURE_CORE_OK " + checks.Count);

sealed class FactorySnapshot : EditorHierarchySnapshot
{
    public static int Captures;
    public FactorySnapshot(GameObject root, EditorHierarchySnapshot? previous)
        : base(root, previous, target => { Captures++; return EditorObjectSnapshot.Capture(target); }) { }
}

static class QueryMeasurement
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long Bytes, double Milliseconds) Measure(Component removable, Component required)
    {
        var timer = System.Diagnostics.Stopwatch.GetTimestamp();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100000; index++)
            if (!EditorObjectStructureSnapshot.CanRemove(removable) || EditorObjectStructureSnapshot.CanRemove(required))
                throw new Exception("Structural query drifted during repeated read-only calls");
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return (bytes, System.Diagnostics.Stopwatch.GetElapsedTime(timer).TotalMilliseconds);
    }
}
