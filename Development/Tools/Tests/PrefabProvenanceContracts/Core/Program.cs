using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;
using BEngine.Serialization;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name), "Independent consumer has no Core private-access grant");
Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a => a.Name is "BEngine.Core.Editor" or "BEngine.Graphics"), "Core provenance needs no Editor or graphics package");
Check(PrefabAssetOperations.GetSourceIdentity(null) == (null, null), "Null objects have no source identity");
Check(!PrefabAssetOperations.IsInstanceRoot(null) && PrefabAssetOperations.GetInstanceReference(null) is null, "Null instance queries are empty");
using var sources = new Scene("sources"); using var live = new Scene("live");
var source = sources.CreateGameObject("source"); var sourceValue = source.AddComponent<PrefabProbe>(); sourceValue.Count = 11;
var sourceChild = sources.CreateGameObject("child"); sourceChild.transform.SetParent(source.transform, false);
sourceChild.AddComponent<PrefabAdded>().Value = 23;
var asset = PrefabDocumentSnapshot.Capture(source, Guid.NewGuid()).CreateAsset("memory.prefab.yaml");
Check(PrefabAssetOperations.GetSourceIdentity(asset) == (null, null), "The asset object is not an instance");
var unrelated = live.CreateGameObject("unrelated");
Check(PrefabAssetOperations.GetSourceIdentity(unrelated) == (null, null) && !PrefabAssetOperations.IsInstanceRoot(unrelated), "An unlinked root is not an instance boundary");
var root = asset.Instantiate(live); var component = root.GetComponent<PrefabProbe>()!;
var child = root.transform.children[0].gameObject;
Check(PrefabAssetOperations.GetSourceIdentity(root) == (asset.assetId, source.Id), "Root identity points to its asset and source object");
Check(PrefabAssetOperations.GetSourceIdentity(component) == (asset.assetId, sourceValue.Id), "Component identity points to its source component");
Check(PrefabAssetOperations.GetSourceIdentity(root.transform) == (asset.assetId, source.transform.Id), "Transform identity is retained");
Check(PrefabAssetOperations.GetSourceIdentity(child).Source == sourceChild.Id, "Child identity is independent of its live ID");
Check(PrefabAssetOperations.IsInstanceRoot(root) && !PrefabAssetOperations.IsInstanceRoot(child), "Root and ordinary child boundaries differ");
Check(PrefabAssetOperations.GetInstanceReference(root) == "memory.prefab.yaml" && PrefabAssetOperations.GetInstanceReference(child) is null, "Only the root carries an explicit source address");
root.transform.SetParent(unrelated.transform, false);
Check(PrefabAssetOperations.IsInstanceRoot(root), "A connected root remains a boundary under an ordinary parent");
var nested = asset.Instantiate(live); nested.transform.SetParent(root.transform, false);
Check(PrefabAssetOperations.IsInstanceRoot(nested), "Nested instances of the same asset remain distinct boundaries");
Check(!PrefabAssetOperations.IsInstanceRoot(nested.transform.children[0].gameObject), "A nested instance's ordinary child remains inside its boundary");
var copy = BObject.Instantiate(root);
Check(PrefabAssetOperations.GetSourceIdentity(copy) == PrefabAssetOperations.GetSourceIdentity(root), "Object cloning preserves source identity");
Check(PrefabAssetOperations.GetInstanceReference(copy) == PrefabAssetOperations.GetInstanceReference(root) && copy.Id != root.Id, "Cloning preserves the source address while remapping live IDs");
BObject.DestroyImmediate(copy);
var detached = PrefabAssetOperations.LoadContents(asset);
Check(PrefabAssetOperations.GetInstanceReference(detached) == "memory.prefab.yaml" && !PrefabAssetOperations.IsInstanceRoot(detached), "Detached authoring contents retain an address without becoming a connected instance");
Check(PrefabAssetOperations.GetSourceIdentity(detached) == (null, null), "Detached contents do not claim a connected source identity");
var detachedId = detached.Id; var detachedChild = detached.transform.children[0].gameObject;
PrefabAssetOperations.ConnectHierarchy(detached, asset);
Check(PrefabAssetOperations.IsInstanceRoot(detached) && detached.Id == detachedId, "Connecting resolved contents retains its live root identity");
Check(PrefabAssetOperations.GetSourceIdentity(detachedChild).Source == sourceChild.Id, "Connecting resolved contents maps retained child identities");
Check(ReferenceEquals(detached.transform.children[0].gameObject, detachedChild), "Connecting reuses the borrowed hierarchy");
var identity = PrefabAssetOperations.GetSourceIdentity(root);
Throws<ArgumentNullException>(() => PrefabAssetOperations.ConnectHierarchy(null!, asset), "Null connection roots are rejected");
Throws<ArgumentNullException>(() => PrefabAssetOperations.ConnectHierarchy(root, (PrefabAsset)null!), "Null connection assets are rejected");
var deadAsset = PrefabDocumentSnapshot.Capture(source, Guid.NewGuid()).CreateAsset(""); BObject.DestroyImmediate(deadAsset);
Throws<ObjectDisposedException>(() => PrefabAssetOperations.ConnectHierarchy(root, deadAsset), "Destroyed connection assets are rejected");
Check(PrefabAssetOperations.GetSourceIdentity(root) == identity, "Rejected connections leave borrowed identity intact");
var deadRoot = live.CreateGameObject("dead"); BObject.DestroyImmediate(deadRoot);
Throws<ObjectDisposedException>(() => PrefabAssetOperations.ConnectHierarchy(deadRoot, asset), "Destroyed connection roots are rejected");
_ = MeasureQueries(root, component, asset.assetId, source.Id);
_ = MeasureQueries(root, component, asset.assetId, source.Id);
var samples = new (bool Valid, long Bytes)[3];
for (var i = 0; i < samples.Length; i++) samples[i] = MeasureQueries(root, component, asset.assetId, source.Id);
Check(samples.All(sample => sample.Valid && sample.Bytes == 0), "Repeated read-only provenance queries allocate no managed memory after warmup");
PrefabAssetOperations.DisconnectHierarchy(root, completely: false);
Check(PrefabAssetOperations.GetSourceIdentity(root) == (null, null) && PrefabAssetOperations.GetInstanceReference(root) is null, "Unpacking removes the selected outer connection");
Check(PrefabAssetOperations.IsInstanceRoot(nested), "Partial unpack preserves an independently connected nested instance");
Check(PrefabAssetOperations.GetSourceIdentity(nested.GetComponent<PrefabProbe>()).Asset == asset.assetId &&
    PrefabAssetOperations.GetSourceIdentity(nested.transform.children[0].gameObject).Source == sourceChild.Id,
    "Partial unpack preserves the nested instance's component and descendant provenance");
Check(PrefabAssetOperations.GetSourceIdentity(child) == (null, null) && PrefabAssetOperations.GetSourceIdentity(component) == (null, null),
    "Partial unpack clears ordinary outer children and components");
PrefabAssetOperations.DisconnectHierarchy(root);
Check(PrefabAssetOperations.GetSourceIdentity(nested) == (null, null) && PrefabAssetOperations.GetInstanceReference(nested) is null, "Complete unpack removes nested connection metadata");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Core.Editor" or "BEngine.Graphics"), "The actual query/connection run loaded no Editor or GPU");
BObject.DestroyImmediate(detached); BObject.DestroyImmediate(asset);
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks, QueryIterationsPerSample = 100000, WarmupSamples = 2, AllocationSamples = samples.Select(sample => sample.Bytes).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PREFAB_PROVENANCE_CORE_OK|" + checks.Count);

static bool Query(GameObject root, Component component, Guid asset, Guid source) =>
    PrefabAssetOperations.GetSourceIdentity(root) == (asset, source) && PrefabAssetOperations.GetSourceIdentity(component).Asset == asset &&
    PrefabAssetOperations.IsInstanceRoot(root) && PrefabAssetOperations.GetInstanceReference(root) is not null;

[MethodImpl(MethodImplOptions.NoInlining)]
static (bool Valid, long Bytes) MeasureQueries(GameObject root, Component component, Guid asset, Guid source)
{
    var before = GC.GetAllocatedBytesForCurrentThread(); var valid = true;
    for (var index = 0; index < 100000; index++) valid &= Query(root, component, asset, source);
    return (valid, GC.GetAllocatedBytesForCurrentThread() - before);
}
