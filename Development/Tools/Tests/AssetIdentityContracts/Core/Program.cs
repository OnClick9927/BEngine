using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform.DotNet;
using BEngine.Serialization;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize(); RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName == Assembly.GetExecutingAssembly().GetName().Name), "Consumer has no Core friendship");
Check(RuntimeServiceContributions.GetDefault<IManagedAssetDocumentCodec>()?.GetType().Assembly == typeof(BObject).Assembly,
    "Headless Core provides its built-in source-document codec");
var record = new EditorAssetInfo(Guid.NewGuid(), "Assets/value.asset.yaml", "virtual://source", "virtual://artifact", "ManagedAsset", false, null, 0);
var asset = new IdentityAsset(); EditorAssetBinding.BindImported(asset, record);
Check(asset.Id == record.Guid && asset.guid == record.Guid.ToString("N"), "Imported GUID and object ID are consistent");
Check(asset.assetPath == record.AssetPath && asset.sourcePath == record.SourcePath && asset.artifactPath == record.ArtifactPath, "Imported paths are committed together");
Check(asset.assetType == record.AssetType && asset.name == "value.asset", "Imported type and default name retain original binding rules");
var plain = new IdentityObject(); EditorAssetBinding.BindImported(plain, record);
Check(plain.Id == record.Guid, "Non-asset objects receive their imported identity");
var captured = new Scene("memory"); var root = captured.CreateGameObject("prefab");
var prefab = PrefabDocumentSnapshot.Capture(root).CreateAsset(""); EditorAssetBinding.BindImported(prefab, record);
Check(prefab.Id == record.Guid && prefab.assetId == record.Guid, "Prefab object and document IDs are committed together");
var embeddedId = Guid.NewGuid(); EditorAssetBinding.BindEmbedded(asset, record, embeddedId, 100001);
Check(asset.Id == embeddedId && asset.guid == record.Guid.ToString("N"), "Embedded objects use a distinct object ID and owner GUID");
Check(RuntimeAssetReferences.TryGetSubAssetIdentity(asset, out var owner, out var local) && owner == record.Guid && local == 100001, "Embedded owner and local identity are queryable");
Check(asset.sourcePath == record.SourcePath && asset.artifactPath == record.ArtifactPath, "Embedding retains existing physical origins");
EditorAssetBinding.BindEmbedded(plain, record, embeddedId, 100001);
Check(plain.Id == embeddedId, "Embedded plain-object identity is restored");
EditorAssetBinding.BindImported(asset, record);
Check(!RuntimeAssetReferences.TryGetSubAssetIdentity(asset, out _, out _) && asset.Id == record.Guid, "Reimport removes stale sub-asset identity");
var before = (asset.Id, asset.assetPath, asset.guid, asset.sourcePath, asset.artifactPath);
foreach (var invalid in new[] { record with { Guid = Guid.Empty }, record with { AssetPath = " " }, record with { SourcePath = null! }, record with { ArtifactPath = null! }, record with { AssetType = null! } })
{
    Throws<ArgumentException>(() => EditorAssetBinding.BindImported(asset, invalid), "Invalid imported metadata is rejected: " + invalid);
    Check(before == (asset.Id, asset.assetPath, asset.guid, asset.sourcePath, asset.artifactPath), "Rejected import has no partial identity change");
}
foreach (var invalid in new[] { record with { Guid = Guid.Empty }, record with { ParentGuid = Guid.NewGuid() }, record with { AssetPath = " " } })
    Throws<ArgumentException>(() => EditorAssetBinding.BindEmbedded(asset, invalid, embeddedId, 1), "Invalid embedded owner rejected: " + invalid);
Throws<ArgumentException>(() => EditorAssetBinding.BindEmbedded(asset, record, Guid.Empty, 1), "Empty embedded ID rejected");
Throws<ArgumentException>(() => EditorAssetBinding.BindEmbedded(asset, record, embeddedId, 0), "Zero local ID rejected");
Throws<ArgumentException>(() => EditorAssetBinding.BindEmbedded(asset, record, embeddedId, -1), "Negative local ID rejected");
Check(before == (asset.Id, asset.assetPath, asset.guid, asset.sourcePath, asset.artifactPath), "Rejected embedded binding leaves identity intact");
Throws<ArgumentNullException>(() => EditorAssetBinding.BindImported(null!, record), "Null import object rejected");
Throws<ArgumentNullException>(() => EditorAssetBinding.BindEmbedded(null!, record, embeddedId, 1), "Null embedded object rejected");
var destroyed = new IdentityAsset(); BObject.DestroyImmediate(destroyed);
Throws<ObjectDisposedException>(() => EditorAssetBinding.BindImported(destroyed, record), "Destroyed imported object rejected");
Throws<ObjectDisposedException>(() => EditorAssetBinding.BindEmbedded(destroyed, record, embeddedId, 1), "Destroyed embedded object rejected");
var node = new IdentityNode { Value = 31 }; node.Next = node;
asset.Count = 52; asset.First = node; asset.Second = node;
var yaml = ManagedAssetSerialization.Serialize(asset);
var restored = (IdentityAsset)ManagedAssetSerialization.DeserializeObject(yaml);
Check(restored.Count == 52 && restored.Restored == 1, "Managed decoding restores values and callback");
Check(ReferenceEquals(restored.First, restored.Second) && ReferenceEquals(restored.First, restored.First!.Next), "Managed graph decoding preserves sharing and cycles");
Check(restored.assetPath == "" && restored.Id != asset.Id, "Decoding does not invent storage identity");
var plainYaml = ManagedAssetSerialization.Serialize((BObject)new IdentityObject { Count = 73 });
Check(((IdentityObject)ManagedAssetSerialization.DeserializeObject(plainYaml)).Count == 73, "Managed format supports plain BObject assets");
Check(ManagedAssetSerialization.GetAssetType(plainYaml) == typeof(IdentityObject), "Managed type query preserves plain-object format");
Throws<InvalidDataException>(() => ManagedAssetSerialization.Deserialize<IdentityAsset>(plainYaml), "Typed asset decoding rejects unrelated types before creation");
var legacy = YamlUtility.Serialize(new { Format = "BEngine.ManagedAsset", Version = 1, TypeName = typeof(IdentityObject).AssemblyQualifiedName, Data = YamlUtility.Serialize(new IdentityObject { Count = 89 }) });
Check(((IdentityObject)ManagedAssetSerialization.DeserializeObject(legacy)).Count == 89, "Legacy version-one managed documents remain readable");
Throws<InvalidDataException>(() => ManagedAssetSerialization.DeserializeObject(yaml.Replace("BEngine.ManagedAsset", "Invalid.ManagedAsset")), "Invalid managed format rejected");
Throws<InvalidDataException>(() => ManagedAssetSerialization.DeserializeObject(YamlUtility.Serialize(new { TypeName = "Missing.Asset, Missing.Assembly" })), "Unloaded managed type rejected");
asset.MarkTransient(); Throws<InvalidOperationException>(() => ManagedAssetSerialization.Serialize((BObject)asset), "Transient managed assets remain unsavable");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.IMGUI.Editor"), "Identity and graph contracts run without graphics or GUI packages");
captured.Dispose();
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("ASSET_IDENTITY_CORE_OK|" + checks.Count);
