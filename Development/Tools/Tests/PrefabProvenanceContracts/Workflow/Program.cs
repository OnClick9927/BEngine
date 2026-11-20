using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;
using AssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); Directory.SetCurrentDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize(); AssetPipelineRuntime.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform, TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
PlayerHostServices.ConfigureDataDirectory(Path.Combine(output, "Project/Assets")); Directory.CreateDirectory(Application.dataPath);
var codec = new RecordingCodec(RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>()!);
var authoring = new RecordingAuthoring(RuntimeServiceContributions.GetDefault<IPrefabAuthoringBackend>()!);
RuntimeServiceContributions.RegisterDefault<IPrefabDocumentCodec>(codec);
RuntimeServiceContributions.RegisterDefault<IPrefabAuthoringBackend>(authoring);
var bridge = typeof(AssetDatabase).Assembly.GetType("BEngine.Editor.EditorBridge", true)!;
var host = DispatchProxy.Create(typeof(AssetDatabase).Assembly.GetType("BEngine.Editor.IEditorHost", true)!, typeof(PrefabHost));
var proxy = (PrefabHost)host; proxy.Root = Path.GetDirectoryName(Application.dataPath)!;
Directory.SetCurrentDirectory(proxy.Root);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
string FileFor(string name) => Path.Combine(Application.dataPath, name + ".prefab.yaml");
bool SameFile(string left, string right) => Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
using var sources = new Scene("sources"); using var scene = new Scene("instances");
var source = sources.CreateGameObject("source"); var value = source.AddComponent<PrefabProbe>(); value.Count = 17;
var child = sources.CreateGameObject("child"); child.transform.SetParent(source.transform, false); value.Target = child;
var updates = 0; void OnUpdate(GameObject _) => updates++;
PrefabUtility.prefabInstanceUpdated += OnUpdate; Undo.ClearAll();
try
{
    Check(typeof(PrefabUtility).Assembly.GetName().Name == "BEngine.Core.Editor", "The prefab workflow belongs to Core Editor");
    Check(!PrefabUtility.IsPartOfAnyPrefab(null) && !PrefabUtility.IsAnyPrefabInstanceRoot(null), "Facade null queries preserve empty results");
    var path = FileFor("base"); var rootId = source.Id;
    var connected = PrefabUtility.SaveAsPrefabAssetAndConnect(source, path, InteractionMode.AutomatedAction, out var saved);
    Check(saved && ReferenceEquals(connected, source) && File.Exists(path), "Hostless save uses the registered codec and retains the borrowed root");
    Check(codec.Saves == 1 && updates == 1, "Hostless save performs one source commit and one instance notification");
    Check(source.Id == rootId && PrefabUtility.IsAnyPrefabInstanceRoot(source), "Hostless save connects the existing live identity");
    var componentPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.transform);
    Console.WriteLine($"HOSTLESS_SOURCE_REFERENCE|actual={componentPath}|expected={path}|stored={PrefabAssetOperations.GetInstanceReference(source)}|child={PrefabAssetOperations.GetSourceIdentity(child)}|root={PrefabAssetOperations.GetSourceIdentity(source)}");
    Check(SameFile(componentPath, path), "Component path lookup resolves the nearest connected root");
    var asset = codec.Inner.Load(path);
    Check(PrefabUtility.IsPartOfAnyPrefab(asset) && PrefabUtility.IsPartOfPrefabAsset(asset) && !PrefabUtility.IsPartOfPrefabInstance(asset), "Asset and connected-instance classification remain distinct");
    var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
    Check(ReferenceEquals(Selection.activeGameObject, instance) && updates == 2, "Instantiation preserves selection and one update notification");
    var liveChild = instance.transform.children[0].gameObject;
    Check(ReferenceEquals(PrefabUtility.GetNearestPrefabInstanceRoot(liveChild), instance), "Child lookup finds the nearest instance");
    var nested = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene); nested.transform.SetParent(instance.transform, false);
    Check(PrefabUtility.IsAnyPrefabInstanceRoot(nested) && ReferenceEquals(PrefabUtility.GetNearestPrefabInstanceRoot(nested), nested), "Nested same-asset instances keep their own boundaries");
    Check(ReferenceEquals(PrefabUtility.GetOutermostPrefabInstanceRoot(nested), instance), "Outermost lookup crosses nested instance boundaries");
    var beforeLoads = codec.Loads; var contents = PrefabUtility.LoadPrefabContents(path);
    Check(codec.Loads == beforeLoads + 1 && contents.name == "source", "Hostless content loading uses the registered source codec");
    Check(!PrefabUtility.IsPartOfPrefabInstance(contents) && SameFile(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(contents), path), "Authoring contents retain source provenance without an instance flag");
    PrefabUtility.UnloadPrefabContents(contents); Check(contents.isDestroyed, "Content unloading destroys its owned hierarchy");
    var variant = PrefabUtility.SaveAsPrefabVariant(instance, FileFor("variant"), connect: false);
    Check(authoring.Saves == 1 && variant.isVariant && File.Exists(FileFor("variant")), "Variant creation uses the registered authoring backend");
    Check(PrefabUtility.GetPrefabAssetType(variant) == PrefabAssetType.Variant, "Saved variants preserve type classification");
    Check(SameFile(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance), path), "Unconnected variant save leaves the original instance's source intact");
    bridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
    const string address = "Assets/hosted.prefab.yaml";
    var hosted = scene.CreateGameObject("hosted"); hosted.AddComponent<PrefabAdded>().Value = 32;
    var previousSaves = codec.Saves; var previousUpdates = updates;
    PrefabUtility.SaveAsPrefabAssetAndConnect(hosted, address, InteractionMode.AutomatedAction, out saved);
    Check(saved && proxy.Imports.Count == 2 && codec.Saves == previousSaves + 2, "Hosted save preserves the synchronous import, stable GUID rewrite and second import sequence");
    var record = proxy.Records[address];
    Check(PrefabAssetOperations.GetSourceIdentity(hosted).Asset == record.Guid && codec.Inner.Load(record.SourcePath).assetId == record.Guid, "Imported GUID, persisted document ID and live connection agree");
    Check(PrefabAssetOperations.GetInstanceReference(hosted) == "guid:" + record.Guid.ToString("N"), "Imported connections retain their GUID address");
    Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hosted) == address && updates == previousUpdates + 1, "GUID address resolution remains in the editor workflow");
    Check(PrefabUtility.GetPrefabInstanceStatus(hosted) == PrefabInstanceStatus.Connected && EditorSceneManager.activeScene is null, "Hosted connection status does not require a default scene window");
    var beforeFailure = File.ReadAllText(record.SourcePath); previousUpdates = updates; codec.FailSave = true;
    try { Throws<IOException>(() => PrefabUtility.SavePrefabAsset(hosted), "Source-provider failures propagate through the facade"); }
    finally { codec.FailSave = false; }
    Check(File.ReadAllText(record.SourcePath) == beforeFailure && updates == previousUpdates, "A failed save leaves the source and update count intact");
    Check(PrefabAssetOperations.GetSourceIdentity(hosted).Asset == record.Guid, "A failed save retains the existing live connection");
    Throws<InvalidDataException>(() => PrefabUtility.SaveAsPrefabAsset(hosted, Path.Combine(output, "outside.prefab.yaml")), "The hosted workflow still rejects paths outside Assets");
    Check(!File.Exists(Path.Combine(output, "outside.prefab.yaml")), "Rejected paths create no source document");
    var hostedVariant = PrefabUtility.SaveAsPrefabVariant(hosted, "Assets/hosted-variant.prefab.yaml");
    Check(authoring.Saves == 3 && hostedVariant.isVariant, "Hosted variant creation preserves its two-stage import and authoring calls");
    Check(PrefabAssetOperations.GetSourceIdentity(hosted).Asset == proxy.Records["Assets/hosted-variant.prefab.yaml"].Guid, "Hosted variant connects to the imported variant identity");
    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
    Check(!PrefabUtility.IsPartOfPrefabInstance(instance) && PrefabUtility.IsPartOfPrefabInstance(nested), "Partial unpack preserves nested instance status");
    PrefabUtility.UnpackPrefabInstance(nested, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
    Check(!PrefabUtility.IsPartOfAnyPrefab(nested) && !Undo.canUndo, "Automated complete unpack preserves no-user-history policy");
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.EditorWorkbench.Editor" or "BEngine.Graphics" or "BEngine.IMGUI.Editor"), "The actual source workflow runs without a Workbench or GPU");
    RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
    Check(ReferenceEquals(RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>(), codec.Inner) && ReferenceEquals(RuntimeServiceContributions.GetDefault<IPrefabAuthoringBackend>(), authoring.Inner), "Removing temporary adapters restores the underlying registered services");
    RuntimeServiceContributions.UnregisterAssembly(codec.Inner.GetType().Assembly);
    Throws<InvalidOperationException>(() => PrefabUtility.LoadPrefabContents(FileFor("missing-provider")), "Missing document providers are reported without an internal Core wrapper");
    Throws<InvalidOperationException>(() => PrefabUtility.SaveAsPrefabAsset(source, FileFor("no-provider")), "Missing source-write providers are reported");
    Check(!File.Exists(FileFor("no-provider")), "Missing providers create no partial source document");
}
finally
{
    PrefabUtility.prefabInstanceUpdated -= OnUpdate;
    bridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
    RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
}
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PREFAB_PROVENANCE_WORKFLOW_OK|" + checks.Count);

public sealed class RecordingCodec(IPrefabDocumentCodec inner) : IPrefabDocumentCodec
{
    public IPrefabDocumentCodec Inner { get; } = inner;
    public int Loads, Saves; public bool FailSave;
    public PrefabAsset Load(string path) { Loads++; return Inner.Load(path); }
    public PrefabAsset Deserialize(string text, string sourcePath) => Inner.Deserialize(text, sourcePath);
    public string Serialize(PrefabAsset prefab) => Inner.Serialize(prefab);
    public string Serialize(GameObject root, Guid? assetId) => Inner.Serialize(root, assetId);
    public void Save(PrefabAsset prefab, string path) { CheckSave(); Inner.Save(prefab, path); }
    public PrefabAsset Save(GameObject root, string path, Guid? assetId) { CheckSave(); return Inner.Save(root, path, assetId); }
    private void CheckSave() { if (FailSave) throw new IOException("Fixture source provider rejected save"); Saves++; }
    public Guid? ReadAssetId(string path) => Inner.ReadAssetId(path);
}
public sealed class RecordingAuthoring(IPrefabAuthoringBackend inner) : IPrefabAuthoringBackend
{
    public IPrefabAuthoringBackend Inner { get; } = inner; public int Saves;
    public PrefabAsset SaveVariant(GameObject root, PrefabAsset basePrefab, string path, Guid? assetId)
    { Saves++; return Inner.SaveVariant(root, basePrefab, path, assetId); }
}
// Only catalog/import bookkeeping is simulated. Source files, graph encoding, identity,
// cache, variants and the workflow facade are real engine implementations.
public class PrefabHost : DispatchProxy
{
    public string Root = ""; public readonly List<string> Imports = [];
    public readonly Dictionary<string, EditorAssetInfo> Records = new(StringComparer.OrdinalIgnoreCase);
    private object? Import(string address)
    {
        Imports.Add(address); var id = Records.TryGetValue(address, out var existing) ? existing.Guid : Guid.NewGuid();
        var path = Path.Combine(Root, address); Records[address] = new(id, address, path, path, "Prefab", false, null, 0);
        YamlUtility.Save(new AssetMetaDocument { Guid = id.ToString("N"), AssetType = "Prefab" }, path + ".meta"); return null;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_ProjectRootPath" => Root, "get_AssetsRootPath" => Path.Combine(Root, "Assets"),
        "GetAsset" => args![0] is Guid id ? Records.Values.Cast<EditorAssetInfo?>().FirstOrDefault(a => a!.Value.Guid == id) : Records.TryGetValue((string)args[0]!, out var record) ? (EditorAssetInfo?)record : null,
        "FindAssets" => Records.Values.ToArray(), "ImportAsset" => Import((string)args![0]!),
        _ => method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null
    };
}
