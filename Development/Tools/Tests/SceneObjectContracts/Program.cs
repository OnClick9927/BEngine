using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.UIElements;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action call, string name) where T : Exception
{ try { call(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform()
    .AddSingleton(new GraphicsUtility(new DrawingPlatform())).BuildServiceProvider();
PlatformRuntime.Initialize(services);
RuntimeHelpers.RunModuleConstructor(typeof(Sprite).Module.ModuleHandle);
RuntimeHelpers.RunModuleConstructor(typeof(UIDocument).Module.ModuleHandle);
var probeName = Assembly.GetExecutingAssembly().GetName().Name;
foreach (var assembly in new[] { typeof(BObject).Assembly, typeof(Sprite).Assembly, typeof(UIDocument).Assembly })
    Check(!assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == probeName),
        assembly.GetName().Name + " grants no private access to this consumer");
Check(typeof(BObject).Assembly.GetType("BEngine.Sprite") is null && typeof(BObject).Assembly.GetType("BEngine.UIElements.VisualTreeAsset") is null,
    "Core owns neither sprite nor UI implementations");
Check(typeof(BObject).GetProperty(nameof(BObject.Id))!.SetMethod?.IsPublic != true,
    "Persistent identity is not a public mutable property");
var identity = new IdentityObject(); var id = Guid.NewGuid(); var instance = identity.GetInstanceID();
identity.Hydrate(id);
Check(identity.Id == id && identity.GetInstanceID() == instance && ReferenceEquals(BObject.FindObjectFromInstanceID(instance), identity),
    "Type-owned hydration preserves runtime identity and registration");
BObject.DestroyImmediate(identity);
Throws<ObjectDisposedException>(() => identity.Hydrate(Guid.NewGuid()), "Destroyed identities cannot be restored");

var rootAsset = new OwnedAsset(); var child = new OwnedAsset(); var shared = new OwnedAsset(); var borrowed = new OwnedAsset();
rootAsset.Owned.Add(child); rootAsset.Owned.Add(shared); child.Owned.Add(shared); shared.Owned.Add(rootAsset); rootAsset.Borrowed = borrowed;
rootAsset.MarkTransient();
Check(new[] { rootAsset, child, shared }.All(RuntimeAssetReferences.IsTransient), "Transience follows owned resources");
Check(!RuntimeAssetReferences.IsTransient(borrowed), "Borrowed resources remain persistent");
Check(rootAsset.Visits == 1 && child.Visits == 1 && shared.Visits == 1, "Shared and cyclic ownership visits each resource once");
Throws<InvalidOperationException>(rootAsset.ValidateSave, "Transient root cannot be saved");
Throws<InvalidOperationException>(child.ValidateSave, "Owned transient child cannot be saved");
borrowed.ValidateSave(); Check(true, "Borrowed source can still be saved");
var late = new OwnedAsset(); child.Owned.Add(late); rootAsset.MarkTransient();
Check(RuntimeAssetReferences.IsTransient(late), "Repeated marking includes children restored after the first pass");
var sourceId = Guid.NewGuid(); RuntimeAssetReferences.BindSource(borrowed, "Assets/source.asset.yaml", "source", "artifact", sourceId, "OwnedAsset");
var mirror = new OwnedAsset(); var mirrorId = mirror.Id; mirror.CopyReference(borrowed);
Check(mirror.Id == mirrorId && mirror.Id != borrowed.Id && mirror.assetPath == borrowed.assetPath && mirror.guid == borrowed.guid,
    "Derived clone copies source reference without reusing source identity");
mirror.MarkTransient(); Check(!RuntimeAssetReferences.IsTransient(borrowed), "Transient clone does not mark its source");
BObject.DestroyImmediate(mirror);
Throws<ObjectDisposedException>(mirror.MarkTransient, "Destroyed resources cannot become transient");
Throws<ObjectDisposedException>(() => mirror.CopyReference(borrowed), "Destroyed mirrors reject reference copying");
var chain = Enumerable.Range(0, 10000).Select(_ => new OwnedAsset()).ToArray();
for (var index = 1; index < chain.Length; index++) chain[index - 1].Owned.Add(chain[index]);
chain[0].MarkTransient();
Check(chain.All(RuntimeAssetReferences.IsTransient), "Deep ownership graphs are processed without recursive stack growth");
foreach (var asset in chain) BObject.DestroyImmediate(asset);

var values = new[] { new Vector2(3, -7), new Vector2(Fix64.FromRaw(12345678901), Fix64.FromRaw(-9876543210)), Vector2.zero };
foreach (var value in values)
{
    Check(Vector2.Rotate(value, 0) == value && Vector2.Rotate(value, 720) == value, "Full turns preserve exact raw components " + value);
    Check(Vector2.Rotate(value, 90) == new Vector2(-value.y, value.x) && Vector2.Rotate(value, -90) == new Vector2(value.y, -value.x),
        "Quarter turns preserve exact raw components " + value);
    Check(Vector2.Rotate(value, 180) == -value && Vector2.Rotate(value, -540) == -value, "Half turns preserve exact raw components " + value);
    for (var angle = -1440; angle <= 1440; angle += 7)
        if (Vector2.Rotate(value, (Fix64)angle / 3) != LegacyRotate(value, (Fix64)angle / 3)) throw new Exception("Fixed-point rotation changed");
    Check(true, "Fractional rotations retain legacy operation order " + value);
}

using var scene = new Scene("Public scene query", services);
var parent = scene.CreateGameObject("Parent"); var owner = scene.CreateGameObject("Owner"); owner.transform.SetParent(parent.transform, false);
parent.transform.localPosition = new(4, -2); parent.transform.localScale = new(2, -3); parent.transform.localRotation = 31;
owner.transform.localPosition = new(3, 7); owner.transform.localScale = new(-2, 4); owner.transform.localRotation = 17;
owner.transform.GetWorldPose(out var position, out var rotation, out var scale);
Check(position == owner.transform.position && rotation == owner.transform.rotation && scale == owner.transform.lossyScale,
    "Coherent pose matches individual properties with nested negative scale");
var oldVersion = owner.transform.worldVersion; parent.transform.localPosition += Vector2.one;
owner.transform.GetWorldPose(out position, out rotation, out scale);
Check(position == owner.transform.position && owner.transform.worldVersion != oldVersion, "World pose observes parent invalidation");
var standalone = new UIDocument(); Check(!standalone.TryGetGameObject(out _) && standalone.isVisible, "Unattached UI document retains visibility semantics");
var document = owner.AddComponent<UIDocument>(); Check(document.TryGetGameObject(out var foundOwner) && ReferenceEquals(foundOwner, owner), "Attached component exposes its owner through a query");
owner.activeSelf = false; Check(!document.isVisible, "Inactive owner hides a UI document"); owner.activeSelf = true;
document.rootVisualElement.style.width = 640; document.rootVisualElement.style.height = 360;
var textField = new TextField { value = "Hello IME", isPasswordField = true };
textField.style.width = 200; textField.style.height = 30; document.rootVisualElement.Add(textField);
// Trigger the existing focus transition without adding a test-only production API.
typeof(UIDocument).GetMethod("FocusText", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(document, [textField]);
Check(Input.textInputRequest is { Text: "Hello IME", Password: true }, "UI focus publishes native text requests through the public platform service");
_ = UIRenderListBuilder.Build(document.rootVisualElement, 640, 360);
Check(Input.textInputRect is not null, "Caret layout publishes an input-method rectangle through the public service");
document.CancelInteraction();
Check(Input.textInputRequest is null && Input.textInputRect is null, "Cancelling focus clears native text input state");
var first = owner.AddComponent<Camera2D>(); first.priority = 8;
var snapshot = scene.QueryComponents<Camera2D>();
Check(ReferenceEquals(snapshot, scene.QueryComponents<Camera2D>()) && snapshot.Count == 1, "Unchanged scene queries reuse a read-only snapshot");
Throws<NotSupportedException>(() => ((IList<Camera2D>)snapshot)[0] = first, "Component snapshots cannot be mutated through a list cast");
var secondOwner = scene.CreateGameObject("Second camera"); var second = secondOwner.AddComponent<Camera2D>(); second.priority = -2;
var changedSnapshot = scene.QueryComponents<Camera2D>();
Check(!ReferenceEquals(snapshot, changedSnapshot) && snapshot.Count == 1 && changedSnapshot.Count == 2, "New membership leaves older snapshots intact");
Check(EngineRenderer.ResolveGameCamera(scene).Position == second.transform.position, "Camera selection retains priority ordering");
var cameras = new List<Camera2D>(); EngineRenderer.FillGameCameras([scene], cameras);
Check(cameras.Count == 2 && ReferenceEquals(cameras[0], second) && ReferenceEquals(cameras[1], first), "Camera collection retains stable sorting");
for (var i = 0; i < 500; i++) { var sprite = scene.CreateGameObject("Sprite").AddComponent<SpriteRenderer>(); sprite.transform.position = new(i % 20 - 10, i / 20 - 12); sprite.size = new(1, 1); }
using var device = new RecordingDevice(); using var renderer = new PortableSceneRenderer(device);
for (var i = 0; i < 20; i++) renderer.Render(scene, RenderCamera.Default, 640, 360, false, false, false, false);
Check(device.Draws > 0 && device.Vertices > 0, "Public component queries feed actual scene rendering");
var watch = new Stopwatch(); var allocated = GC.GetAllocatedBytesForCurrentThread(); watch.Start();
for (var i = 0; i < 200; i++) renderer.Render(scene, RenderCamera.Default, 640, 360, false, false, false, false);
watch.Stop(); var renderBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
Check(renderBytes == 0, "200 warm recording-RHI frames with 500 sprites allocate zero bytes");
File.WriteAllText(Path.Combine(output, "Measurements.json"), JsonSerializer.Serialize(new { Sprites = 500, Frames = 200, Bytes = renderBytes, Milliseconds = watch.Elapsed.TotalMilliseconds }));
long MeasureQueries()
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++) { var items = scene.QueryComponents<SpriteRenderer>(); for (var j = 0; j < items.Count; j++) _ = items[j].enabled; }
    return GC.GetAllocatedBytesForCurrentThread() - before;
}
_ = MeasureQueries(); _ = MeasureQueries(); var queryBytes = MeasureQueries();
Console.WriteLine($"COMPONENT_QUERY_BYTES|{queryBytes}");
Check(queryBytes == 0, "10000 indexed component scans allocate zero bytes");
var destroyedOwner = scene.CreateGameObject("Destroyed pose"); var destroyedTransform = destroyedOwner.transform;
BObject.DestroyImmediate(destroyedOwner);
Throws<ObjectDisposedException>(() => destroyedTransform.GetWorldPose(out _, out _, out _), "Destroyed transforms reject coherent-pose queries");

var metadata = new AssetReferenceMetadata { Guid = Guid.NewGuid().ToString("N"), Settings = new() { ["textureType"] = "Sprite", ["spritePivotX"] = "0.25", ["spritePivotY"] = "0.75", ["spriteBorderLeft"] = "2" } };
var imagePath = Path.Combine(output, "image.png"); var image = Texture.FromRgba32(2, 2, new byte[16]); File.WriteAllBytes(imagePath, image.EncodeToPNG());
var originalResolver = RuntimeServiceContributions.GetDefault<IAssetReferenceResolver>(); var resolver = new Resolver(imagePath, metadata);
RuntimeServiceContributions.RegisterDefault<IAssetReferenceResolver>(resolver);
try
{
    Check(RuntimeAssetReferences.TryParseSubAssetReference("guid:" + metadata.Guid + "#SUBASSET=21300000", out _, out var localId) && localId == 21300000, "Child references preserve case-insensitive suffix parsing");
    Check(!RuntimeAssetReferences.TryParseSubAssetReference("Assets/a.png#subasset=-1", out _, out _) && !RuntimeAssetReferences.TryParseSubAssetReference("Assets/a.png#subasset=0", out _, out _), "Invalid child IDs are rejected");
    var reference = $"guid:{metadata.Guid}#subasset=21300000";
    var sprite = (Sprite)RuntimeObjectCodecs.ReadReference(typeof(Sprite), reference)!;
    Check(sprite is not null && sprite.Id == Guid.Parse(metadata.Guid) && sprite.OwnerGuid == metadata.Guid && sprite.LocalIdentifier == 21300000,
        "Source child sprite preserves owner GUID and local ID");
    Check(sprite!.pivot == new Vector2(Fix64.One / 4, (Fix64)3 / 4) && sprite.border.x == 2, "Source sprite applies owner metadata");
    Check(resolver.ChildReads > 0 && resolver.MetadataReads > 0, "Child metadata and ordinary import metadata retain distinct resolver entry points");
    Check(RuntimeObjectCodecs.Find(typeof(Sprite))!.WriteReference(sprite) == reference, "Sprite reference serialization remains GUID-based");
    Check(ReferenceEquals(RuntimeObjectCodecs.ReadReference(typeof(Sprite), reference), sprite), "Sprite weak cache reuses a live child");
    BObject.DestroyImmediate(sprite);
    Check(RuntimeObjectCodecs.ReadReference(typeof(Sprite), reference) is Sprite refreshed && !ReferenceEquals(refreshed, sprite), "Destroyed weak-cache sprites are rebuilt");
    resolver.Advance();
    Check(RuntimeObjectCodecs.ReadReference(typeof(Sprite), reference) is Sprite, "Resolver generation invalidates child lookups");
}
finally { if (originalResolver is not null) RuntimeServiceContributions.RegisterDefault(originalResolver); BAsset.ClearLoadedAssets(); }
BObject.DestroyImmediate(image);

var stylePath = Path.Combine(output, "panel.bcss"); File.WriteAllText(stylePath, "Label { color: #ff0000; }");
var treePath = Path.Combine(output, "panel.bxml"); File.WriteAllText(treePath, "<UXML><Style src=\"panel.bcss\"/><VisualElement><Label text=\"Hello\"/></VisualElement></UXML>");
var tree = VisualTreeAsset.Load(treePath); var sourceSheet = tree.Instantiate().styleSheets.Single();
var clone = BObject.Instantiate(tree); var ownedSheet = clone.Instantiate().styleSheets.Single();
Check(!ReferenceEquals(sourceSheet, ownedSheet), "UI clone owns an independent stylesheet");
Check(clone.Id != tree.Id, "UI clone retains an independent object identity");
clone.MarkTransient();
Check(RuntimeAssetReferences.IsTransient(clone.inlineStyleSheet) && RuntimeAssetReferences.IsTransient(ownedSheet), "UI-owned inline and external cloned styles become transient");
Check(!RuntimeAssetReferences.IsTransient(tree) && !RuntimeAssetReferences.IsTransient(sourceSheet), "Source UI tree and borrowed stylesheet remain persistent");
var representation = clone.GetImportedRepresentations().Single();
Check(RuntimeAssetReferences.TryGetSubAssetIdentity(representation, out var parentId, out var representationId) && parentId == clone.Id && representationId == 919748196331920512,
    "Inline stylesheet retains its stable imported representation ID");
var notifications = 0; ownedSheet.destroyed += _ => notifications++; BObject.DestroyImmediate(clone); BObject.DestroyImmediate(clone);
Check(ownedSheet.isDestroyed && notifications == 1 && !sourceSheet.isDestroyed, "Destroying a UI clone releases owned styles once and preserves sources");
foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32, new UTF32Encoding(true, true) })
{
    var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("Label { color: #123456; }")).ToArray();
    Check(RuntimeAssetDecoding.TryDecodeRegistered(bytes, "test.bcss", typeof(StyleSheet), out var style), encoding.WebName + " style decoding uses the neutral text codec");
    BObject.DestroyImmediate(style);
}
BObject.DestroyImmediate(tree); BObject.DestroyImmediate(sourceSheet); BObject.DestroyImmediate(standalone);
foreach (var asset in new[] { rootAsset, child, shared, borrowed, late }) BObject.DestroyImmediate(asset);
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"SCENE_OBJECT_CONTRACTS_OK|{checks.Count}");

static Vector2 LegacyRotate(Vector2 value, Fix64 degrees)
{
    degrees %= 360; if (degrees > 180) degrees -= 360; else if (degrees <= -180) degrees += 360;
    if (degrees == 0) return value; if (degrees == 90) return new(-value.y, value.x);
    if (degrees == -90) return new(value.y, -value.x); if (degrees == 180) return -value;
    var radians = degrees * Fix64.Deg2Rad; var cosine = Fix64.Cos(radians); var sine = Fix64.Sin(radians);
    return new(value.x * cosine - value.y * sine, value.x * sine + value.y * cosine);
}
sealed class IdentityObject : BObject { public void Hydrate(Guid id) => RestorePersistentIdentity(id); }
sealed class OwnedAsset : ScriptableObject
{
    [SerializationIgnore] public List<BAsset> Owned { get; } = [];
    [SerializationIgnore] public BAsset? Borrowed { get; set; }
    public int Visits;
    protected override void VisitOwnedAssets(Action<BAsset> visitor) { Visits++; foreach (var owned in Owned) visitor(owned); }
    public void ValidateSave() => EnsureAssetCanBeSaved();
    public void CopyReference(BAsset source) => CopyAssetReferenceFrom(source);
}
sealed class Resolver(string path, AssetReferenceMetadata metadata) : IAssetReferenceResolver
{
    public int ChildReads, MetadataReads; public long Generation { get; private set; }
    public void Advance() => Generation++;
    public BAsset? Load(string address, Type type) => type == typeof(Texture) ? RuntimeAssetDecoding.LoadFile(path, type) : null;
    public bool TryReadMetadata(string address, out AssetReferenceMetadata result) { MetadataReads++; result = metadata; return true; }
    public AssetReferenceMetadata? ReadSubAssetMetadata(string address) { ChildReads++; return metadata; }
    public string? ResolveGuidPath(Guid guid) => guid == Guid.Parse(metadata.Guid) ? path : null;
    public Guid ResolveIdentity(string address, Type type) => Guid.Parse(metadata.Guid);
    public bool CanPrepareAsync(Type type) => false;
    public BTask<BAsset?> LoadPreparedAsync(string address, Type type, AsyncLoadContext context) => BTask<BAsset?>.FromResult(null);
    public BAsset? LoadByGuid(string guid, Type type) => null;
    public BAsset? LoadSubAsset(string address, long id, Type type) => null;
    public void Invalidate(string address) => Generation++;
    public void Unload(BObject target) { }
    public void PruneMissingFiles() { }
    public void Clear() => Generation++;
}
