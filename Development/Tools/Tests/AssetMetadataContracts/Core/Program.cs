using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Serialization;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);
DotNetPlatformServices.Initialize();RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name) where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==Assembly.GetExecutingAssembly().GetName().Name),"Metadata consumer has no private Core grant");
Check(new[] { typeof(AssetReferenceMetadata), typeof(AssetReferenceSubAsset), typeof(RuntimeProjectData),
    typeof(SortingLayerData), typeof(WindowData) }.All(type =>
        type.Assembly.GetName().Name == "BEngine.Core" && Type.GetType($"{type.FullName}, BEngine.Core") == type),
    "Asset and project document DTOs belong to Serialization and preserve old assembly names");
var identity=typeof(QueryAsset).AssemblyQualifiedName!;
var asset=new QueryAsset();asset.Numbers.AddRange(Enumerable.Range(0,2000));
var yaml=ManagedAssetSerialization.Serialize(asset);QueryAsset.Constructions=0;QueryAsset.Restores=0;
Check(ManagedAssetSerialization.GetSerializedTypeName(yaml)==identity,"Current graph header retains exact stored identity");
Check(QueryAsset.Constructions==0 && QueryAsset.Restores==0,"Header reading does not construct or restore managed objects");
var legacy=YamlUtility.Serialize(new{Format="BEngine.ManagedAsset",Version=1,TypeName=identity,Data="{}"});
Check(ManagedAssetSerialization.GetSerializedTypeName(legacy)==identity,"Version-one identity remains readable");
Check(ManagedAssetSerialization.GetSerializedTypeName("typeName: Unloaded.Asset, Missing.Assembly\n")=="Unloaded.Asset, Missing.Assembly","Missing legacy format field keeps its existing default");
Check(ManagedAssetSerialization.GetSerializedTypeName("format: Other\ntypeName: X\n")==null,"Foreign format has no managed identity");
Check(ManagedAssetSerialization.GetSerializedTypeName("format: BEngine.ManagedAsset\n")==null,"Missing identity is rejected");
Check(ManagedAssetSerialization.GetSerializedTypeName("typeName: '  '\n")==null,"Whitespace identity is rejected");
const string unknown="Unloaded.Asset`1[[Other.Value, Retired.Assembly]], Missing.Assembly, Version=1.2.3.4";
Check(ManagedAssetSerialization.GetSerializedTypeName($"typeName: '{unknown}'\n")==unknown,"Unloaded generic identity is not resolved or split");
Check(ManagedAssetSerialization.GetSerializedTypeName("version: 500\ntypeName: Unloaded.Asset\n")=="Unloaded.Asset","Metadata inspection does not pretend to validate a future payload version");
Check(ManagedAssetSerialization.GetSerializedTypeName("typeName: Unloaded.Asset\ngraph: { nodes: [ a, b, c ], unknown: { x: y } }\n")=="Unloaded.Asset","Header projection does not deserialize the object graph");
Throws<ArgumentNullException>(()=>ManagedAssetSerialization.GetSerializedTypeName(null!),"Null input is rejected");
Throws<InvalidDataException>(()=>ManagedAssetSerialization.GetSerializedTypeName("[broken yaml"),"Malformed YAML errors keep the public document exception contract");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Graphics" or "BEngine.Core.Editor"),"Core metadata reading needs no editor or graphics");

for(var i=0;i<25;i++){_=ManagedAssetSerialization.GetSerializedTypeName(yaml);_=ManagedAssetSerialization.GetAssetType(yaml);}
var samples=new List<object>();
(long Bytes,double Milliseconds) Measure(Action action){var before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();for(var i=0;i<50;i++)action();watch.Stop();return(GC.GetAllocatedBytesForCurrentThread()-before,watch.Elapsed.TotalMilliseconds);}
long headerBytes=0,fullBytes=0;
for(var i=0;i<5;i++){
 var header=Measure(()=>{if(ManagedAssetSerialization.GetSerializedTypeName(yaml)!=identity)throw new Exception("header");});
 var full=Measure(()=>{if(ManagedAssetSerialization.GetAssetType(yaml)!=typeof(QueryAsset))throw new Exception("type");});
 headerBytes+=header.Bytes;fullBytes+=full.Bytes;samples.Add(new{Header=header,FullGraphMetadata=full});
}
Check(headerBytes<fullBytes,"Header-only query allocates less than decoding the complete graph DTO");
Check(QueryAsset.Constructions==0 && QueryAsset.Restores==0,"Repeated metadata queries have no object lifecycle callbacks");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,HeaderBytes=headerBytes,FullGraphMetadataBytes=fullBytes,Samples=samples},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
Console.WriteLine("ASSET_METADATA_CORE_OK|"+checks.Count);
public sealed class QueryAsset:BAsset,ISerializationCallbackReceiver
{
 public static int Constructions,Restores;public QueryAsset(){Constructions++;}
 public List<int> Numbers=new();public void OnBeforeSerialize(){}public void OnAfterDeserialize(){Restores++;}
}
