using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);
DotNetPlatformServices.Initialize();
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action operation,string name)where T:Exception{try{operation();}catch(T){Check(true,name);return;}throw new Exception(name);}
var asset=new ReferenceAsset();var original=asset.Id;var instance=asset.GetInstanceID();
RuntimeAssetReferences.EnsureAssetType(asset,"ReferenceAsset");
RuntimeAssetReferences.BindReference(asset," Assets\\Unresolved\\thing.bin ");
Check(asset.assetPath=="Assets/Unresolved/thing.bin","Address normalization preserves unresolved project references");
Check(asset.Id==original&&asset.guid==string.Empty,"Address binding does not invent a persistent GUID");
Check(asset.GetInstanceID()==instance&&!asset.isDestroyed,"Address binding retains object registration");
Check(asset.name=="thing","Address binding retains normal unnamed-asset naming");
Check(asset.assetType=="ReferenceAsset"&&asset.sourcePath==string.Empty&&asset.artifactPath==string.Empty,"Reference binding does not invent source or artifact data");
var absent=Path.Combine(output,"never-created","missing.bin");
RuntimeAssetReferences.BindReference(asset,"builtin://reference/retained",absent);
Check(asset.assetPath=="builtin://reference/retained"&&asset.sourcePath==absent,"Virtual addresses can retain an explicit unresolved physical source");
Check(!File.Exists(absent)&&!Directory.Exists(Path.GetDirectoryName(absent)),"Binding performs no source reads, writes or directory creation");
RuntimeAssetReferences.BindReference(asset,"Assets/Another.bin");
Check(asset.sourcePath==absent,"Omitted source preserves prior source addressing");
RuntimeAssetReferences.BindReference(asset,"Assets/Another.bin",string.Empty);
Check(asset.sourcePath==string.Empty,"Explicit empty source clears prior source addressing");
var id=Guid.NewGuid();RuntimeAssetReferences.BindSource(asset,"Assets/Existing.bin","source","artifact",id,"custom");
RuntimeAssetReferences.BindReference(asset,"Assets/Renamed.bin");
Check(asset.Id==id&&asset.guid==id.ToString("N")&&asset.artifactPath=="artifact"&&asset.sourcePath=="source"&&asset.assetType=="custom","Readdressing preserves explicitly bound identity and imported information");
var owner=Guid.NewGuid();RuntimeAssetReferences.BindSubAsset(asset,"Assets/Owner.bin",owner,7,id);
RuntimeAssetReferences.BindReference(asset,"Assets/Root.bin");
Check(!RuntimeAssetReferences.TryGetSubAssetIdentity(asset,out _,out _)&&asset.Id==id,"Root reference binding clears a prior child association");
var address=asset.assetPath;
Throws<ArgumentNullException>(()=>RuntimeAssetReferences.BindReference(null!,"a"),"Null asset is rejected");
Throws<ArgumentNullException>(()=>RuntimeAssetReferences.BindReference(asset,null!),"Null address is rejected before mutation");
Check(asset.assetPath==address,"Invalid arguments leave addressing unchanged");
BObject.DestroyImmediate(asset);
Throws<ObjectDisposedException>(()=>RuntimeAssetReferences.BindReference(asset,"a"),"Destroyed references cannot be rebound");
Check(asset.Releases==1,"Core resource destruction remains once-only");
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
Check(RuntimeTypeCache.FindType("legacy-preview-contract-interface")==typeof(IAliasProbe),"Extension type aliases support interfaces without Core knowing the extension");
Check(RuntimeTypeCache.ResolveSerializedType("legacy-preview-contract-interface")==typeof(IAliasProbe),"Serialized interface aliases resolve through the existing type mechanism");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Core.Editor" or "BEngine.Graphics" or "BEngine.IMGUI.Editor"),"Reference contracts run without Editor, Graphics, window or GPU assemblies");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PREVIEW_CORE_CONTRACTS_OK|"+checks.Count);

public sealed class ReferenceAsset:BAsset{public int Releases;protected override void ReleaseAssetResources()=>Releases++;}
[RuntimeTypeAlias("legacy-preview-contract-interface")]
public interface IAliasProbe;
