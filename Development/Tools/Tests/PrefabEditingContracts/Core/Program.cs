using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform.DotNet;
using BEngine.Serialization;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize();
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
var coreEditor = typeof(EditorPrefabEditSession).Assembly;
Check(!coreEditor.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name),
    "Independent consumer has no private-access grant");
Check(Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a => a.Name == "BEngine.Core.Editor"),
    "The editing probe uses the Core Editor assembly");
Check(typeof(BObject).Assembly.GetType(typeof(EditorPrefabEditSession).FullName!) is null,
    "Prefab authoring sessions are absent from Core Runtime");
foreach (var type in new[] { typeof(EditorPrefabEditSession), typeof(EditorPrefabRefresh) })
{
    Check(type.Assembly == coreEditor, type.Name + " is owned by Core Editor");
    Check(!type.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(), type.Name + " exposes no mutable data fields");
    Check(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
        .All(t => t.IsVisible), type.Name + " has no internal type in its public contract");
}
Throws<ArgumentNullException>(() => EditorPrefabRefresh.Prepare(null!), "Null refresh roots are rejected");
Throws<ArgumentNullException>(() => EditorPrefabEditSession.CaptureHierarchy(null!), "Null captures are rejected");
using var sourceScene = new Scene("source"); using var scene = new Scene("instance");
var root = sourceScene.CreateGameObject("root");
var child = sourceScene.CreateGameObject("child"); child.transform.SetParent(root.transform, false);
var sibling = sourceScene.CreateGameObject("sibling"); sibling.transform.SetParent(root.transform, false);
var value = root.AddComponent<PrefabProbe>(); value.Count = 17; value.Target = child;
root.AddComponent<PrefabTracked>();
child.transform.localPosition = new Vector2(3, 5);
var captured = PrefabDocumentSnapshot.Capture(root, Guid.NewGuid());
var asset = captured.CreateAsset("memory.prefab.yaml");
var instance = asset.Instantiate(scene);
var unrelated = scene.CreateGameObject("unrelated");
Throws<InvalidDataException>(() => EditorPrefabEditSession.Open(unrelated, asset), "An unrelated root with no matching source identity is rejected");
Check(PrefabTracked.Last!.isDestroyed, "A failed session constructor releases its partially prepared owned contents");
Check(!unrelated.isDestroyed && !asset.isDestroyed, "A failed session constructor leaves borrowed inputs intact");
var instanceValue = instance.GetComponent<PrefabProbe>()!;
var instanceChild = instance.transform.children[0].gameObject;
var parent = scene.CreateGameObject("outside parent"); instance.transform.SetParent(parent.transform, false);
Check(EditorPrefabEditSession.HasInstanceReference(instance), "Instantiation retains opaque source provenance");
Check(EditorPrefabRefresh.Prepare(parent) is null, "Unconnected roots have no refresh work");
var snapshot = EditorPrefabEditSession.CaptureHierarchy(instance);
Check(snapshot.Count == 3 && snapshot.ParentOf(instance.Id) == parent.Id, "Hierarchy capture retains the external parent identity");
snapshot.SetParent(instance.Id, null);
Check(ReferenceEquals(instance.transform.parent, parent.transform), "Editing a detached snapshot does not mutate live hierarchy");
instance.name = "local"; instanceValue.Count = 99;
var extra = scene.CreateGameObject("local extra"); extra.transform.SetParent(instance.transform, false);
var unwanted = instance.AddComponent<PrefabAdded>();
using (var edit = EditorPrefabEditSession.Open(instance, asset))
{
    Check(ReferenceEquals(edit.Root, instance) && ReferenceEquals(edit.Asset, asset), "A session retains borrowed root and asset");
    Check(!ReferenceEquals(edit.Contents, root) && !ReferenceEquals(edit.Contents, instance), "A session owns independent detached contents");
    Check(edit.TrySource(instance, out var mapped) && ReferenceEquals(mapped, edit.Contents), "Instance root resolves to its source object");
    Check(edit.TryLive(mapped, out var back) && ReferenceEquals(back, instance), "Source mapping round trips to the same live root");
    Check(!edit.TrySource(extra, out _), "Unapplied additions do not acquire a false source mapping");
    Check(ReferenceEquals(edit.ToSource(asset), asset) && ReferenceEquals(edit.ToLive(asset), asset), "Asset references remain borrowed");
    var childId = instanceChild.Id; var valueId = instanceValue.Id;
    edit.RestoreHierarchy(CopyValues);
    Check(instance.name == "root" && instanceValue.Count == 17, "Restoration uses caller-provided serialized-value copying");
    Check(ReferenceEquals(instance.GetComponent<PrefabProbe>(), instanceValue) && instanceValue.Id == valueId,
        "Restoration preserves retained component identity");
    Check(ReferenceEquals(instance.transform.children[0].gameObject, instanceChild) && instanceChild.Id == childId,
        "Restoration preserves retained GameObject identity");
    Check(ReferenceEquals(instanceValue.Target, instanceChild), "Object references map to live instance identities");
    Check(instanceChild.transform.localPosition == new Vector2(3, 5), "Fixed-point Transform values survive reconciliation");
    Check(ReferenceEquals(instance.transform.parent, parent.transform), "Restoration leaves the instance's external parent intact");
    Check(instance.GetComponent<PrefabAdded>() is null && extra.isDestroyed, "Restoration removes local structure absent from its source");
    Check(instance.transform.childCount == 2, "Restoration introduces no duplicate children");
    Check(edit.SourceObject(edit.Contents.Id) == edit.Contents, "Source lookup returns the owned object for its ID");
    Throws<ArgumentNullException>(() => edit.RestoreHierarchy(null!), "Missing value-copy policy is rejected");
    Throws<ArgumentNullException>(() => edit.CopySubtree(null!, false, CopyValues), "Null subtree roots are rejected");
}
using (var edit = EditorPrefabEditSession.Open(instance, asset))
{
    var added = instance.AddComponent<PrefabAdded>(); added.Value = 41;
    var sourceComponent = edit.AddSourceComponent(added);
    CopyValues(added, sourceComponent, edit.ToSource);
    Check(edit.TrySource(added, out var mapped) && ReferenceEquals(mapped, sourceComponent), "Adding a source component records its mapping");
    Check(sourceComponent.Id == added.Id && ((PrefabAdded)sourceComponent).Value == 41, "Applied component identity and copied values are retained");
    instance.RemoveComponent(added);
    var restored = edit.RestoreInstanceComponent(sourceComponent, instance);
    CopyValues(sourceComponent, restored, edit.ToLive);
    Check(restored.Id == added.Id && ((PrefabAdded)restored).Value == 41, "Removed components can be reconstructed with their recorded identity");
    Check(edit.TryLive(sourceComponent, out var live) && ReferenceEquals(live, restored), "Restored component replaces its mapping target");
    Throws<ArgumentException>(() => edit.RestoreInstanceComponent(sourceComponent, parent), "An unrelated instance owner is rejected");
    var addedRoot = scene.CreateGameObject("added subtree"); addedRoot.transform.SetParent(instance.transform, false);
    var addedChild = scene.CreateGameObject("added child"); addedChild.transform.SetParent(addedRoot.transform, false);
    var a = addedRoot.AddComponent<PrefabProbe>(); a.Target = addedChild;
    var b = addedChild.AddComponent<PrefabProbe>(); b.Target = a;
    edit.CopySubtree(addedRoot, true, CopyValues);
    var mappedRoot = (GameObject)edit.Source(addedRoot);
    Check(mappedRoot.transform.childCount == 1, "A subtree copy retains child structure");
    Check(ReferenceEquals(mappedRoot.GetComponent<PrefabProbe>()!.Target, edit.Source(addedChild)), "Subtree forward references map after all identities are registered");
    Check(ReferenceEquals(((PrefabProbe)edit.Source(b)).Target, edit.Source(a)), "Subtree component back references retain cycles");
    var addedId = addedRoot.Id; var childId = addedChild.Id;
    BObject.DestroyImmediate(addedRoot);
    // Reopen after deletion so removed live objects are absent from the live map, as in the workflow.
    var changed = PrefabDocumentSnapshot.Capture(edit.Contents, asset.assetId).CreateAsset("memory2.prefab.yaml");
    using var restore = EditorPrefabEditSession.Open(instance, changed);
    restore.CopySubtree((GameObject)restore.SourceObject(mappedRoot.Id), false, CopyValues);
    var rebuilt = instance.transform.children.Single(t => t.gameObject.Id == addedId).gameObject;
    Check(rebuilt.transform.children[0].gameObject.Id == childId, "Restoring a deleted subtree retains all recorded live IDs");
    Check(ReferenceEquals(rebuilt.GetComponent<PrefabProbe>()!.Target, rebuilt.transform.children[0].gameObject), "Recreated subtree references resolve within the recreated hierarchy");
    BObject.DestroyImmediate(changed);
}
var disposed = EditorPrefabEditSession.Open(instance, asset); var owned = disposed.Contents;
disposed.Dispose(); disposed.Dispose();
Check(owned.isDestroyed && !instance.isDestroyed && !asset.isDestroyed, "Disposal releases only owned source contents and is idempotent");
Throws<ObjectDisposedException>(() => disposed.TrySource(instance, out _), "Disposed mapping queries are rejected");
Throws<ObjectDisposedException>(() => disposed.RestoreHierarchy(CopyValues), "Disposed sessions cannot edit their instance");
BObject.DestroyImmediate(asset);
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PREFAB_CORE_OK {checks.Count}");

static void CopyValues(BObject source, BObject target, Func<BObject, BObject> map)
{
    target.name = source.name;
    if (source is GameObject a && target is GameObject b)
    { b.SetActive(a.activeSelf); b.tag = a.tag; b.layer = a.layer; b.isStatic = a.isStatic; }
    if (source is Component c && target is Component d) d.enabled = c.enabled;
    if (source is Transform t && target is Transform u)
    { u.localPosition = t.localPosition; u.localRotation = t.localRotation; u.localScale = t.localScale; }
    if (source is PrefabProbe p && target is PrefabProbe q)
    { q.Count = p.Count; q.Target = p.Target is null ? null : map(p.Target); }
    if (source is PrefabAdded x && target is PrefabAdded y) y.Value = x.Value;
}
