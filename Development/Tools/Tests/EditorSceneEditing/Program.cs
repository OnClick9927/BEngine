using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize();
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool passed, string name) { if (!passed) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
Check(typeof(EditorSceneEditing).Assembly == typeof(EditorObjectSnapshot).Assembly,
    "Scene editing mechanisms belong to Core Editor");
Check(typeof(Scene).Assembly.GetType(typeof(EditorSceneEditing).FullName!) is null,
    "Scene editing does not enter Core Runtime");
Check(!typeof(Scene).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name),
    "The consumer has no runtime friendship grant");
Check(!typeof(EditorSceneEditing).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name),
    "The consumer has no editor friendship grant");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Host"),
    "The contract runs without graphics, IMGUI or a workbench");
var provider = new ScopeProvider();
Throws<ArgumentNullException>(() => EditorSceneEditing.CreateUnloadedScene(null!, "x", provider), "Null names are rejected before scope allocation");
Throws<ArgumentNullException>(() => EditorSceneEditing.CreateUnloadedScene("x", null!, provider), "Null paths are rejected before scope allocation");
Check(provider.Created == 0, "Invalid references do not leak scopes");
var absent = Path.Combine(output, "never-created", "Reference.scene.yaml");
var reference = EditorSceneEditing.CreateUnloadedScene("reference", absent, provider);
Check(reference.isCreated && !reference.isLoaded && reference.gameObjects.Count == 0, "References are live, unloaded and empty");
Check(reference.name == "reference" && reference.path == absent && !File.Exists(absent), "References retain caller data without file I/O");
Check(provider.Created == 1 && ReferenceEquals(reference.GetService<ScopeToken>(), provider.Token), "References own a scene service scope");
var id = reference.Id;
Throws<InvalidOperationException>(() => EditorSceneEditing.AttachHierarchy(reference, new GameObject()), "Unloaded references reject hierarchy attachment");
Check(reference.Id == id && reference.gameObjects.Count == 0, "Failed attachment preserves reference identity and membership");
EditorSceneEditing.CloseScene(reference);
Check(!reference.isCreated && !reference.isLoaded && provider.Disposed == 1, "Closing a reference releases its scope exactly once");
EditorSceneEditing.CloseScene(reference);
Check(provider.Disposed == 1, "Repeated reference closure is harmless");
Throws<ObjectDisposedException>(() => RuntimeAssetReferences.BindScenePath(reference, "changed"), "Closed references reject path binding");
Throws<ArgumentNullException>(() => EditorSceneEditing.AttachHierarchy(null!, new GameObject()), "Null destinations are rejected");
Throws<ArgumentNullException>(() => EditorSceneEditing.AttachHierarchy(new Scene(), null!), "Null roots are rejected");
Throws<ArgumentNullException>(() => EditorSceneEditing.CloseScene(null!), "Null closure is rejected");

using var scene = new Scene("edit");
var root = new GameObject("root"); var first = new GameObject("first"); var last = new GameObject("last");
first.transform.SetParent(root.transform, false); last.transform.SetParent(root.transform, false);
var leaf = new GameObject("leaf"); leaf.transform.SetParent(first.transform, false);
var value = leaf.AddComponent<SceneValue>(); var borrowed = new BorrowedAsset(); value.Count = 42; value.Borrowed = borrowed;
var objects = new[] { root, first, leaf, last }; var identities = objects.Select(o => (o.Id, o.GetInstanceID())).ToArray();
Throws<ArgumentException>(() => EditorSceneEditing.AttachHierarchy(scene, first), "A non-root cannot introduce a partially detached graph");
Check(scene.gameObjects.Count == 0 && objects.All(o => o.scene is null), "Invalid root rejection is atomic");
EditorSceneEditing.AttachHierarchy(scene, root);
Check(objects.All(o => o.scene == scene) && scene.gameObjects.SequenceEqual(objects), "Attachment binds the complete hierarchy in preorder");
Check(objects.Select(o => (o.Id, o.GetInstanceID())).SequenceEqual(identities), "Attachment preserves persistent and live identities");
Check(root.transform.children.Select(t => t.gameObject).SequenceEqual(new[] { first, last }) && leaf.transform.parent == first.transform,
    "Attachment preserves child and sibling order");
Check(ReferenceEquals(leaf.GetComponent<SceneValue>(), value) && value.Count == 42 && ReferenceEquals(value.Borrowed, borrowed),
    "Attachment preserves component values and borrowed references");
Check(objects.All(o => ReferenceEquals(BObject.FindObjectFromInstanceID(o.GetInstanceID()), o)), "Every attached node is registered");
using var other = new Scene("other");
Throws<InvalidOperationException>(() => EditorSceneEditing.AttachHierarchy(other, root), "Already owned hierarchies cannot be attached elsewhere");
Check(other.gameObjects.Count == 0 && objects.All(o => o.scene == scene), "Ownership rejection preserves both scenes");
Throws<InvalidOperationException>(() => EditorSceneEditing.AttachHierarchy(scene, root), "Repeated attachment does not introduce duplicates");
Check(scene.gameObjects.Count == objects.Length, "Duplicate attachment is rejected before mutation");

var sourceIdentity = Guid.NewGuid();
RuntimeAssetReferences.BindSource(scene, "Assets/source.scene.yaml", "source", "artifact", sourceIdentity, "Scene");
var cacheVersion = BAsset.cacheGeneration;
RuntimeAssetReferences.BindScenePath(scene, "relative/source.scene.yaml");
Check(scene.path == "relative/source.scene.yaml" && scene.Id == sourceIdentity && scene.assetPath == "Assets/source.scene.yaml",
    "Path binding preserves GUID and source addressing");
Check(scene.sourcePath == "source" && scene.artifactPath == "artifact" && scene.assetType == "Scene" && BAsset.cacheGeneration == cacheVersion,
    "Path binding does not import, invalidate or replace document metadata");
scene.MarkTransient();
Check(RuntimeAssetReferences.IsTransient(scene) && objects.All(RuntimeAssetReferences.IsTransient) && RuntimeAssetReferences.IsTransient(value),
    "Public transient marking covers scene objects and components");
Check(!RuntimeAssetReferences.IsTransient(borrowed), "Scene transient marking preserves borrowed source assets");
var late = scene.CreateGameObject("late").AddComponent<SceneValue>();
Check(RuntimeAssetReferences.IsTransient(late), "Late scene objects inherit transient lifetime");
scene.MarkTransient(); Check(scene.gameObjects.Count == 5 && scene.Id == sourceIdentity, "Repeated marking preserves scene identity and structure");

var removed = other.CreateGameObject("undo parent"); var removedId = removed.GetInstanceID();
var component = removed.AddComponent<SceneValue>(); component.Count = 7;
other.Destroy(removed);
Check(removed.scene is null && removed.isDestroyed, "The Undo candidate is detached and destroyed");
EditorSceneEditing.AttachHierarchy(other, removed);
Check(removed.scene == other && !removed.isDestroyed && removed.GetInstanceID() == removedId && component.Count == 7,
    "Attachment supports the workbench parent Undo/Redo restoration contract");
Check(ReferenceEquals(BObject.FindObjectFromInstanceID(removedId), removed), "Restored roots return to the live-object registry");
other.Destroy(removed);
EditorSceneEditing.AttachHierarchy(other, removed);
Check(other.gameObjects.Count == 1, "Repeated parent Undo/Redo does not duplicate membership");

var lifetimeProvider = new ScopeProvider(); var live = new Scene("lifetime", lifetimeProvider);
var lifecycle = live.CreateGameObject().AddComponent<LifecycleValue>();
var runtime = new SceneRuntime(live); runtime.Start();
Check(runtime.IsRunning && lifecycle.Awakes == 1, "A scene runtime starts through the public core contract");
var addedRoot = new GameObject(); var addedChild = new GameObject(); addedChild.transform.SetParent(addedRoot.transform, false);
var addedLifecycle = addedChild.AddComponent<LifecycleValue>();
EditorSceneEditing.AttachHierarchy(live, addedRoot);
Check(addedChild.scene == live && addedLifecycle.Awakes == 1, "Runtime attachment initializes descendant behaviours once");
LifecycleValue.Closing = () => { Check(!live.isLoaded, "Close callbacks observe an unloaded scene"); EditorSceneEditing.CloseScene(live); };
EditorSceneEditing.CloseScene(live); LifecycleValue.Closing = null;
Check(!runtime.IsRunning && !live.isCreated && !live.isLoaded && live.gameObjects.Count == 0, "Closure stops runtime, destroys objects and unloads the scene");
Check(lifecycle.Destroys == 1 && addedLifecycle.Destroys == 1 && lifetimeProvider.Disposed == 1,
    "Reentrant closure releases behaviours and service scope once");
EditorSceneEditing.CloseScene(live); Check(lifetimeProvider.Disposed == 1, "Closed runtime scenes remain idempotent");
var free = new GameObject();
Throws<ObjectDisposedException>(() => EditorSceneEditing.AttachHierarchy(live, free), "Disposed scenes reject attachment");
Check(free.scene is null && !free.isDestroyed, "Rejected attachment does not take ownership of the input");

var failedScope = new ScopeProvider { FailOnDispose = true };
var failure = EditorSceneEditing.CreateUnloadedScene("failure", "unread", failedScope);
Throws<InvalidOperationException>(() => EditorSceneEditing.CloseScene(failure), "Scope cleanup failure remains visible to the host");
Check(!failure.isCreated && !failure.isLoaded, "A cleanup failure cannot reopen or revive a scene");
EditorSceneEditing.CloseScene(failure); Check(failedScope.Disposed == 1, "Cleanup failure does not cause double disposal");

File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"EDITOR_SCENE_EDITING_OK {checks.Count}");

public sealed class SceneValue : MonoBehaviour { public int Count; public BAsset? Borrowed; }
public sealed class BorrowedAsset : BAsset { }
public sealed class LifecycleValue : MonoBehaviour
{
    public static Action? Closing;
    public int Awakes, Destroys;
    public override void Awake() => Awakes++;
    public override void OnDestroy() { Destroys++; Closing?.Invoke(); }
}
public sealed class ScopeToken { }
public sealed class ScopeProvider : IServiceProvider, IServiceScopeFactory
{
    public int Created, Disposed; public bool FailOnDispose; public ScopeToken Token = new();
    public object? GetService(Type type) => type == typeof(IServiceScopeFactory) ? this : type == typeof(ScopeToken) ? Token : null;
    public IServiceScope CreateScope() { Created++; return new OwnedScope(this); }
    sealed class OwnedScope(ScopeProvider owner) : IServiceScope
    {
        public IServiceProvider ServiceProvider => new ScopedProvider(owner);
        public void Dispose() { owner.Disposed++; if (owner.FailOnDispose) throw new InvalidOperationException("scope failure"); }
    }
    sealed class ScopedProvider(ScopeProvider owner) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(ScopeToken) ? owner.Token : null; }
}
