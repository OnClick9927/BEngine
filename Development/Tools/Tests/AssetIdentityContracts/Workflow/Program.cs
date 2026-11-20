using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using AssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); Directory.SetCurrentDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize(); AssetPipelineRuntime.Initialize();
// This isolated host has no editor composition pass; activate the source-format provider explicitly.
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
    Assembly.Load("BEngine.Core.Editor").ManifestModule.ModuleHandle);
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform, TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var bridge = typeof(AssetDatabase).Assembly.GetType("BEngine.Editor.EditorBridge", true)!;
var hostType = typeof(AssetDatabase).Assembly.GetType("BEngine.Editor.IEditorHost", true)!;
var host = DispatchProxy.Create(hostType, typeof(AssetHost)); var proxy = (AssetHost)host;
proxy.ProjectRoot = Path.Combine(output, "Project"); Directory.CreateDirectory(Path.Combine(proxy.ProjectRoot, "Assets"));
PlayerHostServices.ConfigureDataDirectory(Path.Combine(proxy.ProjectRoot, "Assets"));
bridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
var codec = RuntimeServiceContributions.GetDefault<IManagedAssetDocumentCodec>()!;
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception(name); }
string FileFor(string address) => Path.Combine(proxy.ProjectRoot, address);
string Add(string name, string text, string type)
{ var address = "Assets/" + name; File.WriteAllText(FileFor(address), text); proxy.Import(address, type); return address; }
try
{
    Check(codec.GetType().Assembly.GetName().Name == "BEngine.Core", "Managed source persistence belongs to runtime Core");
    Check(typeof(AssetDatabase).Assembly.GetReferencedAssemblies().Any(a => a.Name == "BEngine.Core"),
        "Core Editor facade uses the runtime Core document codec");
    var node = new IdentityNode { Value = 19 }; node.Next = node;
    var asset = new IdentityAsset { name = "native", Count = 43, First = node, Second = node };
    const string native = "Assets/native.asset.yaml";
    AssetDatabase.CreateAsset(asset, native); var record = proxy.Records[native];
    Check(File.Exists(FileFor(native)) && proxy.Imported == native, "Create commits a document then requests import");
    Check(asset.Id == record.Guid && asset.assetPath == native, "Create associates the source object with the imported GUID");
    var yaml = File.ReadAllText(FileFor(native));
    Check(yaml == ManagedAssetSerialization.Serialize(asset), "Source file preserves the Core managed wire representation");
    var loaded = AssetDatabase.LoadAssetAtPath<IdentityAsset>(native)!;
    Check(loaded.Count == 43 && loaded.Restored == 1 && loaded.Id == record.Guid, "Load restores values, callback and imported identity");
    Check(ReferenceEquals(loaded.First, loaded.Second) && ReferenceEquals(loaded.First, loaded.First!.Next), "Source loading preserves shared cyclic graphs");
    Check(loaded.sourcePath == FileFor(native) && loaded.artifactPath == FileFor(native), "Source loading retains physical origins");
    Check(ReferenceEquals(AssetDatabase.LoadMainAssetAtPath(native), loaded), "Repeated facade loads retain weak-cache identity");
    loaded.Count = 92; EditorUtility.SetDirty(loaded);
    Check(AssetDatabase.SaveAsset(loaded) && !EditorUtility.IsDirty(loaded), "Save retains dirty-state policy");
    var reloaded = AssetDatabase.LoadAssetAtPath<IdentityAsset>(native)!;
    Check(!ReferenceEquals(loaded, reloaded) && reloaded.Count == 92 && reloaded.Id == record.Guid, "Save invalidates imported cache while preserving GUID");
    var before = File.ReadAllText(FileFor(native));
    using (var locked = File.Open(FileFor(native), FileMode.Open, FileAccess.Read, FileShare.Read))
        Throws(() => AssetDatabase.SaveAsset(reloaded), "A failed atomic replacement is reported");
    Check(File.ReadAllText(FileFor(native)) == before && !Directory.EnumerateFiles(Application.dataPath, ".*.tmp").Any(), "Failed source save preserves existing file and cleans temporary content");
    var plainAddress = "Assets/plain.asset.yaml"; var plain = new IdentityObject { Count = 75 };
    AssetDatabase.CreateAsset(plain, plainAddress);
    var plainLoaded = AssetDatabase.LoadAssetAtPath<IdentityObject>(plainAddress)!;
    Check(plainLoaded.Count == 75 && plainLoaded.Id == proxy.Records[plainAddress].Guid, "Plain BObject native assets preserve decoding and GUID binding");
    var child = new IdentityAsset { Count = 77 }; var childId = child.Id;
    AssetDatabase.AddObjectToAsset(child, reloaded);
    Check(AssetDatabase.IsSubAsset(child) && child.Id == childId && child.assetPath == native, "Embedded add retains object identity and owner path");
    Check(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(child, out var ownerGuid, out var local) && ownerGuid == record.Guid.ToString("N") && local >= 100000, "Embedded references use owner GUID and stable local identifier");
    var all = AssetDatabase.LoadAllAssetsAtPath(native); var restoredChild = all.OfType<IdentityAsset>().Single(a => a.Id == childId);
    Check(restoredChild.Count == 77 && AssetDatabase.IsSubAsset(restoredChild), "Embedded load restores payload and ownership");
    Check(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(restoredChild, out var restoredGuid, out var restoredLocal) && restoredGuid == ownerGuid && restoredLocal == local, "Embedded reload preserves owner/local address");
    restoredChild.Count = 87; Check(AssetDatabase.SaveAsset(restoredChild), "Embedded save writes its metadata payload");
    AssetDatabase.ImportAsset(native);
    Check(AssetDatabase.LoadAllAssetsAtPath(native).OfType<IdentityAsset>().Single(a => a.Id == childId).Count == 87, "Reimport observes embedded payload updates");
    AssetDatabase.RemoveObjectFromAsset(restoredChild);
    Check(!RuntimeAssetReferences.TryGetSubAssetIdentity(restoredChild, out _, out _) && restoredChild.assetPath == "" && restoredChild.Id == childId, "Embedded removal detaches its address without changing object identity");
    Check(!AssetDatabase.LoadAllAssetsAtPath(native).Any(a => a.Id == childId), "Removed embedded objects disappear from subsequent loads");
    var plainChild = new IdentityObject { Count = 101 }; var plainId = plainChild.Id;
    AssetDatabase.AddObjectToAsset(plainChild, native);
    Check(AssetDatabase.LoadAllAssetsAtPath(native).OfType<IdentityObject>().Single().Count == 101, "Embedded plain BObject payloads remain supported");
    Check(AssetDatabase.LoadAllAssetsAtPath(native).OfType<IdentityObject>().Single().Id == plainId, "Embedded plain BObject IDs are restored");
    var fileChildAddress = Add("attached.asset.yaml", ManagedAssetSerialization.Serialize(new IdentityAsset { Count = 131 }), "ManagedAsset");
    var fileChildMeta = YamlUtility.Load<AssetMetaDocument>(FileFor(fileChildAddress) + ".meta");
    fileChildMeta.ParentGuid = record.Guid.ToString("N"); fileChildMeta.LocalIdentifier = 200000;
    YamlUtility.Save(fileChildMeta, FileFor(fileChildAddress) + ".meta"); proxy.Import(fileChildAddress);
    var fileChild = AssetDatabase.LoadAssetAtPath<IdentityAsset>(fileChildAddress)!;
    Check(fileChild.assetPath == native && fileChild.sourcePath == FileFor(fileChildAddress), "File-backed sub-assets retain their own origin and parent address");
    Check(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(fileChild, out var fileOwner, out var fileLocal) && fileOwner == record.Guid.ToString("N") && fileLocal == 200000, "File-backed sub-asset addressing is preserved");
    var fileChildId = fileChild.Id;
    AssetDatabase.RemoveObjectFromAsset(fileChild);
    Check(fileChild.Id == fileChildId && fileChild.assetPath == fileChildAddress && fileChild.sourcePath == FileFor(fileChildAddress) && !RuntimeAssetReferences.TryGetSubAssetIdentity(fileChild, out _, out _), "File-backed removal restores its main address and retains origin and object identity");
    var legacy = YamlUtility.Serialize(new { Format = "BEngine.ManagedAsset", Version = 1, TypeName = typeof(IdentityAsset).AssemblyQualifiedName, Data = YamlUtility.Serialize(new IdentityAsset { Count = 109 }) });
    var legacyAddress = Add("legacy.asset.yaml", legacy, "ManagedAsset");
    Check(AssetDatabase.LoadAssetAtPath<IdentityAsset>(legacyAddress)!.Count == 109, "Legacy native document loads through registered persistence");
    var bad = Add("broken.asset.yaml", "Format: Invalid\nTypeName: Missing.Type, Missing.Assembly\n", "ManagedAsset");
    Check(AssetDatabase.LoadMainAssetAtPath(bad) is DefaultAsset fallback && fallback.Id == proxy.Records[bad].Guid, "Malformed native document retains the default-asset fallback");
    using var scene = new Scene("authoring"); var root = scene.CreateGameObject("root");
    const string prefabAddress = "Assets/root.prefab.yaml";
    var prefabCodec = RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>()!;
    var prefab = prefabCodec.Save(root, FileFor(prefabAddress), null); proxy.Import(prefabAddress, "Prefab");
    var imported = AssetDatabase.LoadAssetAtPath<PrefabAsset>(prefabAddress)!;
    Check(imported.Id == proxy.Records[prefabAddress].Guid && imported.assetId == imported.Id, "Prefab import binds object and persistent-document IDs together");
    Check(imported.Instantiate(scene).name == "root", "Imported Prefab remains instantiable");
    const string sceneAddress = "Assets/authoring.scene.yaml";
    RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>()!.Save(scene, FileFor(sceneAddress)); proxy.Import(sceneAddress, "Scene");
    var importedScene = AssetDatabase.LoadAssetAtPath<Scene>(sceneAddress)!;
    Check(importedScene.path == sceneAddress && importedScene.Id == proxy.Records[sceneAddress].Guid, "Scene registration preserves logical scene path and imported ID");
    var scriptAddress = Add("Probe.cs", "public sealed class Probe {}", "Script");
    Check(AssetDatabase.LoadMainAssetAtPath(scriptAddress) is MonoScript script && script.text.Contains("class Probe"), "Registered script loading remains available");
    var asmAddress = Add("broken.asmdef.yaml", "[not valid yaml", "AssemblyDefinition");
    Check(AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(asmAddress) is { importError.Length: > 0 }, "Assembly definition import-error representation is retained");
    var textAddress = Add("message.txt", "text payload", "Text");
    Check(AssetDatabase.LoadAssetAtPath<BEngine.Editor.TextAsset>(textAddress)!.text == "text payload", "AssetPipeline text provider remains available");
    var customAddress = Add("registered.identity", "custom", "Identity");
    AssetTypeRegistry.Register<IdentityAsset>(".identity", "Identity", context => new IdentityAsset { Count = 211 });
    var custom = AssetDatabase.LoadAssetAtPath<IdentityAsset>(customAddress)!;
    Check(custom.Count == 211 && custom.Id == proxy.Records[customAddress].Guid, "Registered custom loaders receive the same identity commit");
    AssetTypeRegistry.Unregister(".identity");
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.EditorWorkbench.Editor" or "BEngine.Graphics" or "BEngine.IMGUI.Editor"), "Asset workflows execute without a default Workbench or GPU");
    RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
    Check(ReferenceEquals(RuntimeServiceContributions.GetDefault<IManagedAssetDocumentCodec>(), codec),
        "Unloading a project assembly preserves the built-in Core document codec");
}
finally { bridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]); }
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("ASSET_IDENTITY_WORKFLOW_OK|" + checks.Count);

public class AssetHost : DispatchProxy
{
    public string ProjectRoot = "", Imported = "";
    public readonly Dictionary<string, EditorAssetInfo> Records = new(StringComparer.OrdinalIgnoreCase);
    public void Import(string path, string? type = null)
    {
        Imported = path; var source = Path.Combine(ProjectRoot, path); var prior = Records.GetValueOrDefault(path);
        var id = prior.Guid == Guid.Empty ? Guid.NewGuid() : prior.Guid;
        type ??= prior.AssetType ?? "ManagedAsset";
        Guid? parent = prior.ParentGuid; var local = prior.LocalIdentifier;
        if (File.Exists(source + ".meta"))
        {
            var meta = YamlUtility.Load<AssetMetaDocument>(source + ".meta");
            if (Guid.TryParse(meta.Guid, out var stored)) id = stored;
            parent = Guid.TryParse(meta.ParentGuid, out var owner) ? owner : null; local = meta.LocalIdentifier;
        }
        else YamlUtility.Save(new AssetMetaDocument { Guid = id.ToString("N"), AssetType = type }, source + ".meta");
        Records[path] = new(id, path, source, source, type, false, parent, local);
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_ProjectRootPath" => ProjectRoot,
        "get_AssetsRootPath" => Path.Combine(ProjectRoot, "Assets"),
        "GetAsset" => args![0] is Guid id ? Records.Values.Cast<EditorAssetInfo?>().FirstOrDefault(a => a!.Value.Guid == id) : Records.TryGetValue((string)args[0]!, out var record) ? (EditorAssetInfo?)record : null,
        "FindAssets" => Records.Values.ToArray(),
        "ImportAsset" => ImportAndReturn((string)args![0]!),
        _ => method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null
    };
    private object? ImportAndReturn(string path) { Import(path); return null; }
}
