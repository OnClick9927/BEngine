using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.Serialization;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new InvalidOperationException(name); }
T Complete<T>(BTask<T> operation)
{
    var watch = Stopwatch.StartNew();
    while (!operation.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15))
    { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2)); Thread.Yield(); }
    if (!operation.IsCompleted) throw new TimeoutException("Owner-thread operation stalled.");
    return operation.GetAwaiter().GetResult();
}
DotNetPlatformServices.Initialize(); AssetPipelineRuntime.Initialize();
RuntimeTypeCache.RegisterAssemblies([typeof(ProbeComponent).Assembly]);
PlayerHostServices.ConfigureDataDirectory(Path.Combine(output, "Assets"));
Directory.CreateDirectory(Application.dataPath);
var codec = RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>()!;
var scenes = RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>()!;
var author = RuntimeServiceContributions.GetDefault<IPrefabAuthoringBackend>()!;
var resolver = RuntimeServiceContributions.GetDefault<IAssetReferenceResolver>()!;
Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName == typeof(ProbeComponent).Assembly.GetName().Name), "Independent consumer has no private-access grant");
var detachedTypes = new[] { typeof(SerializedHierarchy), typeof(PrefabReferenceSnapshot),
    typeof(PrefabReferenceSet), typeof(PrefabReferenceSet.Enumerator) };
Check(detachedTypes.All(type => type.Assembly.GetName().Name == "BEngine.Core" &&
    Type.GetType($"{type.FullName}, BEngine.Core") == type),
    "Detached prefab editing types belong to Serialization and retain old assembly names");
foreach (var type in new[] { typeof(SerializedHierarchy), typeof(PrefabDocumentSnapshot), typeof(PrefabReferenceSnapshot), typeof(PrefabReferenceSet), typeof(AssetLoadSlot) })
    Check(!type.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(), type.Name + " hides mutable implementation fields");
Throws<InvalidOperationException>(() => _ = default(SerializedHierarchy).Count, "Default graph handle is rejected");
Throws<InvalidOperationException>(() => default(PrefabDocumentSnapshot).CreateAsset(""), "Default document is rejected");
Throws<InvalidOperationException>(() => _ = default(PrefabReferenceSet).Count, "Default reference set is rejected");
Throws<InvalidOperationException>(() => _ = default(PrefabReferenceSnapshot).Root, "Default reference is rejected");
Throws<InvalidOperationException>(() => default(AssetLoadSlot).TryGetTarget(out _), "Default cache slot is rejected");

using var scene = new Scene("Authoring contracts");
var root = scene.CreateGameObject("Root"); var child = scene.CreateGameObject("Child"); child.transform.SetParent(root.transform);
child.transform.localPosition = new Vector2(Fix64.Parse("1.25"), Fix64.Parse("-2.5"));
root.AddComponent<ProbeComponent>().Target = child;
var snapshot = PrefabDocumentSnapshot.Capture(root, Guid.NewGuid()); snapshot.Validate();
Check(snapshot.Objects.Count == 2 && snapshot.Objects.Depth(child.Id) == snapshot.Objects.Depth(root.Id) + 1, "Capture retains the hierarchy and parent depth");
Check(snapshot.Objects.ParentOf(child.Id) == root.Id && snapshot.Objects.Contains(child.Id), "Graph queries retain object identities");
var graphClone = snapshot.Objects.Clone(); graphClone.SetParent(child.Id, null);
Check(snapshot.Objects.ParentOf(child.Id) == root.Id, "Editable graph clones do not alias their source");
var copy = SerializedHierarchy.FromJson(snapshot.Objects.ToJson());
Check(copy.ParentOf(child.Id) == root.Id && copy.CollectObjectIds().SetEquals(new[] { root.Id, child.Id }), "Merge interchange retains the established graph schema");
var published = snapshot.CreateAsset(""); snapshot.Objects.SetParent(child.Id, null);
using (var target = new Scene("Published snapshot"))
{
    var instance = published.Instantiate(target);
    Check(instance.transform.childCount == 1, "Editing a retained handle cannot mutate an already published prefab");
    var linked = instance.GetComponent<ProbeComponent>()!.Target;
    Check(ReferenceEquals(linked, instance.transform.children[0].gameObject), "Object references remap to cloned prefab objects");
    Check(linked!.transform.localPosition == child.transform.localPosition, "Fixed-point state survives capture and restore");
}
BObject.DestroyImmediate(published);
snapshot = PrefabDocumentSnapshot.Capture(root);
var refMap = PrefabDocumentSnapshot.CaptureIdMap(root);
var reference = PrefabReferenceSnapshot.Create("base.prefab.yaml", root.Id, refMap);
reference.SetBaseline(snapshot.Objects.Clone()); var refClone = reference.Clone();
refClone.SetBaseline(graphClone);
Check(reference.Baseline.ParentOf(child.Id) == root.Id, "Reference clones preserve independent baseline ownership");
var references = snapshot.References; references.Add(reference);
_ = MeasureReferences(references, root.Id);
var (refCount, refBytes) = MeasureReferences(references, root.Id);
Check(refCount == 10000 && refBytes == 0, "Reference enumeration creates no wrapper objects");
references.Remove(root.Id); Check(references.Count == 0, "Reference editing remains local to its detached document");

var path = Path.Combine(Application.dataPath, "Deep", "Documents", "Base.prefab.yaml");
var prefab = codec.Save(root, path, null);
Check(File.Exists(path) && codec.ReadAssetId(path) == prefab.assetId, "Source save creates parent folders and preserves the document GUID");
Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Successful atomic save removes staging files");
var persisted = File.ReadAllText(path);
var loaded = BAsset.Load<PrefabAsset>(path)!;
Check(ReferenceEquals(loaded, BAsset.Load<PrefabAsset>(path)), "Repeated source loads share one cached object");
Check(loaded.assetId == prefab.assetId && loaded.sourcePath == Path.GetFullPath(path), "Metadata-free prefab loads retain serialized identity and physical origin");
Check(codec.Serialize(codec.Deserialize(persisted, path)) == persisted, "Prefab YAML round trip preserves the existing serialized document");
var variantRoot = prefab.Instantiate(scene); variantRoot.name = "Local root override";
var variantPath = Path.Combine(Application.dataPath, "Variant.prefab.yaml");
var variant = author.SaveVariant(variantRoot, prefab, variantPath, null);
Check(variant.isVariant, "Source authoring creates a connected variant");
child.name = "Updated inherited child";
var added = scene.CreateGameObject("Added upstream"); added.transform.SetParent(root.transform);
codec.Save(root, path, null);
using (var target = new Scene("Variant target"))
{
    var instance = variant.Instantiate(target);
    Check(instance.name == "Local root override", "Three-way source merge preserves local overrides");
    Check(instance.transform.children.Any(t => t.gameObject.name == child.name), "Variants inherit changed source properties");
    Check(instance.transform.children.Any(t => t.gameObject.name == "Added upstream"), "Variants inherit newly added source objects");
    Check(instance.GetComponent<ProbeComponent>()!.Target!.name == child.name, "Variant identity maps retain component references");
}
var container = scene.CreateGameObject("Container");
var nested = variant.Instantiate(scene); nested.transform.SetParent(container.transform);
var outerPath = Path.Combine(Application.dataPath, "Outer.prefab.yaml");
var outer = codec.Save(container, outerPath, null);
using (var target = new Scene("Nested target"))
{
    var instance = outer.Instantiate(target);
    Check(instance.transform.childCount == 1 && instance.transform.children[0].childCount == 2, "Nested prefabs retain inherited object structure");
    Check(instance.transform.children[0].gameObject.name == "Local root override", "Nested source merge retains variant overrides");
}
var baseResolver = RuntimeServiceContributions.GetDefault<IPrefabResolutionBackend>()!;
var customBackend = new PrefabBackend(baseResolver, path);
var customCodec = new PrefabCodec(codec);
RuntimeServiceContributions.RegisterDefault<IPrefabResolutionBackend>(customBackend);
RuntimeServiceContributions.RegisterDefault<IPrefabDocumentCodec>(customCodec);
var customVariantPath = Path.Combine(Application.dataPath, "Custom.prefab.yaml");
var customVariant = author.SaveVariant(variantRoot, prefab, customVariantPath, null);
Check(customCodec.Reads == 1 && customCodec.Saves == 1, "Variant authoring honors the active document codec for identity and saving");
Check(customVariant.basePrefab == customBackend.Address, "Variant authoring honors the active backend's reference policy");
codec.Save(variantRoot, customVariantPath, null);
Check(customBackend.Loads == 1, "Saving an existing variant resolves its base through the active backend");
RuntimeServiceContributions.UnregisterAssembly(typeof(PrefabBackend).Assembly);
var scenePath = Path.Combine(Application.dataPath, "Scene", "Authoring.scene.yaml");
scenes.Save(scene, scenePath); var sceneYaml = File.ReadAllText(scenePath);
using (var restored = scenes.Load(scenePath, null))
    Check(restored.Id == scene.Id && restored.gameObjects.Any(g => g.Id == child.Id), "Scene source round trip preserves scene and object GUIDs");
Check(!Directory.EnumerateFiles(Path.GetDirectoryName(scenePath)!, "*.tmp").Any(), "Scene atomic save removes its staging files");
using var transient = new Scene("Transient"); var runtime = new SceneRuntime(transient); runtime.Start();
Throws<InvalidOperationException>(() => Document<Scene>.FromAsset(transient).Write(path, (_, _) => throw new Exception("must not write")), "Transient documents cannot overwrite source data");
Check(File.ReadAllText(path) != "" && File.ReadAllText(scenePath) == sceneYaml, "Rejected source writes preserve existing document bytes");
runtime.Stop();

var aPath = Path.Combine(Application.dataPath, "CycleA.prefab.yaml"); var bPath = Path.Combine(Application.dataPath, "CycleB.prefab.yaml");
var a = PrefabDocumentSnapshot.Capture(root, Guid.NewGuid()); var b = PrefabDocumentSnapshot.Capture(root, Guid.NewGuid());
var aRef = PrefabReferenceSnapshot.Create(RuntimeAssetReferences.ToReferencePath(bPath), root.Id, PrefabDocumentSnapshot.CaptureIdMap(root)); aRef.SetBaseline(a.Objects.Clone()); a.SetBase(aRef);
var bRef = PrefabReferenceSnapshot.Create(RuntimeAssetReferences.ToReferencePath(aPath), root.Id, PrefabDocumentSnapshot.CaptureIdMap(root)); bRef.SetBaseline(b.Objects.Clone()); b.SetBase(bRef);
File.WriteAllText(aPath, a.ToYaml()); File.WriteAllText(bPath, b.ToYaml());
var cyclic = codec.Load(aPath);
Throws<InvalidDataException>(() => { using var unused = new Scene("Cycle"); cyclic.Instantiate(unused); }, "Cyclic source references fail with a bounded validation error");
File.Delete(bPath); BAsset.Invalidate(bPath);
Throws<InvalidDataException>(() => { using var unused = new Scene("Missing"); cyclic.Instantiate(unused); }, "Missing prefab sources report an explicit validation error");

var slot = AssetLoadSlot.Create(); var loads = 0;
var asset = slot.GetOrLoad(() => { loads++; return new ProbeAsset(); })!;
Check(ReferenceEquals(asset, slot.GetOrLoad(() => throw new Exception("cache miss"))) && loads == 1, "Cache slots reuse an existing live object");
var slotCopy = slot; Check(slotCopy.Equals(slot) && !AssetLoadSlot.Create().Equals(slot), "Value handles preserve cache-slot identity");
BObject.DestroyImmediate(asset);
Check(slot.GetOrLoad(() => new ProbeAsset()) is { isDestroyed: false }, "Destroyed cached assets are reloaded");
var failing = AssetLoadSlot.Create();
Throws<IOException>(() => failing.GetOrLoad(() => throw new IOException("expected")), "Loader exceptions propagate");
Check(!failing.IsLoading && failing.GetOrLoad(() => null) is null, "Failures and null returns leave a reusable slot");
var recursive = AssetLoadSlot.Create();
var recovered = recursive.GetOrLoad(() => {
    Throws<InvalidOperationException>(() => recursive.GetOrLoad(() => null), "Recursive loading is rejected");
    Check(recursive.IsLoading, "Rejected inner recursion does not clear outer loading state");
    return new ProbeAsset();
});
Check(ReferenceEquals(recovered, recursive.GetOrLoad(() => null)), "A decoder can recover from inner recursion and publish once");
var warm = MeasureSlot(recursive); var steady = MeasureSlot(recursive);
Check(steady.Allocated == 0 && steady.Hits == 10000, "Warmed cache-slot hits allocate zero bytes");

var source = Path.Combine(Application.dataPath, "Data.probe"); File.WriteAllText(source, "source");
var imported = Path.Combine(output, "Imported.probe"); File.WriteAllText(imported, "artifact");
var artifactResolver = new ArtifactResolver(source, imported);
RuntimeServiceContributions.RegisterDefault<IAssetArtifactResolver>(artifactResolver);
var decodes = 0;
RuntimeAssetCodecRegistry.Register<ProbeAsset>((bytes, _) => { decodes++; return new ProbeAsset { Value = System.Text.Encoding.UTF8.GetString(bytes.Span) }; });
var importedAsset = BAsset.Load<ProbeAsset>(source)!;
Check(importedAsset.Value == "artifact" && importedAsset.sourcePath == source && importedAsset.artifactPath == imported, "Source resolution chooses the imported payload without changing its source identity");
Check(ReferenceEquals(importedAsset, BAsset.Load<ProbeAsset>(source)) && decodes == 1, "Source cache avoids duplicate artifact decoding");
BAsset.Invalidate(source); Check(!ReferenceEquals(importedAsset, BAsset.Load<ProbeAsset>(source)), "Source invalidation discards the old cache slot");
var owner = Guid.NewGuid(); var objectId = Guid.NewGuid();
YamlUtility.Save(new AssetReferenceMetadata { Guid = owner.ToString("N"), SubAssets = [new AssetReferenceSubAsset { Guid = objectId.ToString("N"), LocalIdentifier = 7, TypeName = typeof(ProbeAsset).AssemblyQualifiedName!, Data = "value: embedded\n" }] }, source + ".meta");
BAsset.Invalidate(source);
var sub = BAsset.LoadSubAsset<ProbeAsset>(source, 7)!;
Check(sub.Value == "embedded" && sub.Id == objectId, "Embedded subasset payload and object GUID survive source decoding");
Check(RuntimeAssetReferences.TryGetSubAssetIdentity(sub, out var subOwner, out var local) && subOwner == owner && local == 7, "Subasset owner and local identity remain observable through a read-only query");
Check(ReferenceEquals(sub, BAsset.LoadSubAsset<ProbeAsset>(source, 7)), "Subasset loads retain weak cache identity");
Check(!RuntimeAssetReferences.TryGetSubAssetIdentity(importedAsset, out _, out var mainLocal) && mainLocal == 0, "Main assets are not reported as subassets");
var customIdentity = Guid.NewGuid();
RuntimeServiceContributions.RegisterDefault<IAssetReferenceResolver>(new IdentityResolver(resolver, customIdentity));
var customDocument = Document<ProbeAsset>.Read(source, _ => new ProbeAsset());
Check(customDocument.ToAsset().Id == customIdentity, "Document binding honors a developer-supplied identity resolver");
customDocument.Write(source, (_, fullPath) => Check(fullPath == source, "Document writers receive the canonical physical path"));
Check(customDocument.ToAsset().Id == customIdentity, "Saving retains custom resolver identity semantics");
RuntimeServiceContributions.UnregisterAssembly(typeof(IdentityResolver).Assembly);
RuntimeServiceContributions.RegisterDefault<IAssetArtifactResolver>(artifactResolver);

var deepRoot = scene.CreateGameObject("Deep source");
var deepPaths = Enumerable.Range(0, 65).Select(i => Path.Combine(Application.dataPath, "Deep" + i + ".prefab.yaml")).ToArray();
for (var i = 64; i >= 0; i--)
{
    var document = PrefabDocumentSnapshot.Capture(deepRoot, Guid.NewGuid());
    if (i < 64)
    {
        var inherited = PrefabReferenceSnapshot.Create(RuntimeAssetReferences.ToReferencePath(deepPaths[i + 1]), deepRoot.Id, PrefabDocumentSnapshot.CaptureIdMap(deepRoot));
        inherited.SetBaseline(document.Objects.Clone()); document.SetBase(inherited);
    }
    File.WriteAllText(deepPaths[i], document.ToYaml());
}
using (var destination = new Scene("Depth guard"))
{
    Check(codec.Load(deepPaths[1]).Instantiate(destination).name == deepRoot.name, "The existing 64-document source depth remains supported");
    Throws<InvalidDataException>(() => codec.Load(deepPaths[0]).Instantiate(destination), "The 65th source document is rejected before unbounded recursion");
}

var disposed = 0; var committed = 0;
RuntimeAssetCodecRegistry.RegisterPrepared<ProbeAsset, Payload>((bytes, _) => new ProbeAsset(),
    (bytes, _) => new Payload(() => Interlocked.Increment(ref disposed)), (payload, _) => { committed++; return new ProbeAsset(); });
Check(resolver.CanPrepareAsync(typeof(ProbeAsset)), "Source adapter discovers prepared decoders through the public contract");
var gateCalls = 0;
var asyncAsset = Complete(RuntimeAssetDecoding.DecodeRegisteredAsync("p"u8.ToArray(), source, typeof(ProbeAsset), new(), factory => { gateCalls++; return factory(); }));
Check(asyncAsset is ProbeAsset && committed == 1 && gateCalls == 1, "Prepared commit executes once through the caller's revision gate");
Check(disposed == 1, "Successful prepared payload ownership is released exactly once");
Throws<InvalidOperationException>(() => Complete(RuntimeAssetDecoding.DecodeRegisteredAsync("p"u8.ToArray(), source, typeof(ProbeAsset), new(), _ => throw new InvalidOperationException("stale"))), "A rejected revision gate prevents stale commit");
Check(committed == 1 && disposed == 2, "Rejected prepared commits dispose payloads without creating assets");
BAsset.Invalidate(source);
var pending = resolver.LoadPreparedAsync(source, typeof(ProbeAsset), new()); BAsset.Invalidate(source);
Throws<InvalidOperationException>(() => Complete(pending), "Source changes during asynchronous preparation reject stale publication");
Check(committed == 1 && disposed == 3, "Invalidated source preparations release owned payloads exactly once");
BAsset.Invalidate(source);
var stampPending = resolver.LoadPreparedAsync(source, typeof(ProbeAsset), new());
File.SetLastWriteTimeUtc(imported, File.GetLastWriteTimeUtc(imported).AddMinutes(1));
Throws<InvalidOperationException>(() => Complete(stampPending), "Changed artifact timestamps reject stale asynchronous publication");
Check(committed == 1 && disposed == 4, "Timestamp rejection disposes data before any object commit");
BAsset.Invalidate(source);
using var prepareEntered = new ManualResetEventSlim(); using var prepareRelease = new ManualResetEventSlim();
RuntimeAssetCodecRegistry.RegisterPrepared<ProbeAsset, Payload>((bytes, _) => new ProbeAsset(),
    (bytes, token) => { prepareEntered.Set(); prepareRelease.Wait(token); return new Payload(() => Interlocked.Increment(ref disposed)); },
    (payload, _) => { committed++; return new ProbeAsset(); });
var replacedPending = resolver.LoadPreparedAsync(source, typeof(ProbeAsset), new());
var prepareDeadline = Stopwatch.StartNew();
while (!prepareEntered.IsSet && prepareDeadline.Elapsed < TimeSpan.FromSeconds(15))
{ BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2)); Thread.Yield(); }
Check(prepareEntered.IsSet, "The old decoder has actually begun preparing before replacement");
RuntimeAssetCodecRegistry.Register<ProbeAsset>((_, _) => new ProbeAsset());
prepareRelease.Set();
Throws<InvalidOperationException>(() => Complete(replacedPending), "Decoder replacement during preparation rejects the old commit");
Check(committed == 1 && disposed == 5, "Decoder replacement releases old prepared data exactly once");
RuntimeAssetCodecRegistry.Unregister<ProbeAsset>();
RuntimeAssetCodecRegistry.RegisterPrepared<ProbeAsset, Payload>((bytes, _) => new ProbeAsset(),
    (bytes, _) => new Payload(() => Interlocked.Increment(ref disposed)), (payload, _) => { committed++; return new ProbeAsset(); });
using (var cancellation = new BCancellationSource())
{
    cancellation.Cancel();
    Throws<BOperationCanceledException>(() => Complete(RuntimeAssetDecoding.DecodeRegisteredAsync("p"u8.ToArray(), source, typeof(ProbeAsset), new(cancellation.Token), factory => factory())), "Canceled prepared work never commits");
    Check(committed == 1, "Cancellation leaves source identity and cache publication untouched");
}
RuntimeAssetCodecRegistry.Unregister<ProbeAsset>();
RuntimeServiceContributions.UnregisterAssembly(typeof(ArtifactResolver).Assembly);
Check(RuntimeAssetDecoding.ResolveArtifactPath(source, typeof(ProbeAsset)) is null, "Assembly cleanup removes the artifact resolver contribution");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor"), "Authoring contracts execute without graphics, UI or window initialization");
File.WriteAllText(Path.Combine(output, "Measurements.json"), JsonSerializer.Serialize(new { CacheHits = new { steady.Hits, steady.Allocated, steady.Milliseconds }, ReferenceEnumerationBytes = refBytes }));
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("ASSET_AUTHORING_CONTRACTS_OK|" + checks.Count);

[MethodImpl(MethodImplOptions.NoInlining)]
static (int Count, long Bytes) MeasureReferences(PrefabReferenceSet references, Guid root)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var count = 0;
    for (var i = 0; i < 10000; i++) foreach (var item in references) if (item.Root == root) count++;
    return (count, GC.GetAllocatedBytesForCurrentThread() - allocated);
}
[MethodImpl(MethodImplOptions.NoInlining)]
static (int Hits, long Allocated, double Milliseconds) MeasureSlot(AssetLoadSlot slot)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var timestamp = Stopwatch.GetTimestamp(); var hits = 0;
    for (var i = 0; i < 10000; i++) if (slot.GetOrLoad(static () => throw new Exception("miss")) is not null) hits++;
    return (hits, GC.GetAllocatedBytesForCurrentThread() - allocated, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
}
public sealed class ProbeComponent : Behaviour { public GameObject? Target; }
public sealed class ProbeAsset : BAsset { public string Value = ""; }
sealed class ArtifactResolver(string source, string artifact) : IAssetArtifactResolver
{ public string? Resolve(string path, Type type) => path == source ? artifact : null; }
sealed class Payload(Action dispose) : IDisposable { public void Dispose() => dispose(); }
sealed class IdentityResolver(IAssetReferenceResolver original, Guid id) : IAssetReferenceResolver
{
    public long Generation => original.Generation;
    public Guid ResolveIdentity(string path, Type type) => id;
    public BAsset? Load(string path, Type type) => original.Load(path, type);
    public bool CanPrepareAsync(Type type) => original.CanPrepareAsync(type);
    public BTask<BAsset?> LoadPreparedAsync(string path, Type type, AsyncLoadContext context) => original.LoadPreparedAsync(path, type, context);
    public BAsset? LoadByGuid(string guid, Type type) => original.LoadByGuid(guid, type);
    public BAsset? LoadSubAsset(string path, long localIdentifier, Type type) => original.LoadSubAsset(path, localIdentifier, type);
    public void Invalidate(string path) => original.Invalidate(path);
    public void Unload(BObject target) => original.Unload(target);
    public void PruneMissingFiles() => original.PruneMissingFiles();
    public void Clear() => original.Clear();
    public string? ResolveGuidPath(Guid guid) => original.ResolveGuidPath(guid);
    public bool TryReadMetadata(string path, out AssetReferenceMetadata metadata) => original.TryReadMetadata(path, out metadata);
    public AssetReferenceMetadata? ReadSubAssetMetadata(string path) => original.ReadSubAssetMetadata(path);
}
sealed class PrefabBackend(IPrefabResolutionBackend original, string source) : IPrefabResolutionBackend
{
    public readonly string Address = "guid:" + Guid.NewGuid().ToString("N");
    public int Loads;
    public PrefabDocumentSnapshot Resolve(PrefabAsset asset) => original.Resolve(asset);
    public PrefabAsset Load(string reference) { Loads++; return original.Load(reference == Address ? source : reference); }
    public string Reference(PrefabAsset asset) => Address;
    public void ResolveReferences(SerializedHierarchy objects, PrefabReferenceSet references) => original.ResolveReferences(objects, references);
    public SerializedHierarchy Merge(SerializedHierarchy baseline, SerializedHierarchy current, SerializedHierarchy source) => original.Merge(baseline, current, source);
}
sealed class PrefabCodec(IPrefabDocumentCodec original) : IPrefabDocumentCodec
{
    public int Reads, Saves;
    public PrefabAsset Load(string path) => original.Load(path);
    public PrefabAsset Deserialize(string text, string sourcePath) => original.Deserialize(text, sourcePath);
    public string Serialize(PrefabAsset prefab) => original.Serialize(prefab);
    public string Serialize(GameObject root, Guid? id) => original.Serialize(root, id);
    public void Save(PrefabAsset prefab, string path) { Saves++; original.Save(prefab, path); }
    public PrefabAsset Save(GameObject root, string path, Guid? id) => original.Save(root, path, id);
    public Guid? ReadAssetId(string path) { Reads++; return original.ReadAssetId(path); }
}
