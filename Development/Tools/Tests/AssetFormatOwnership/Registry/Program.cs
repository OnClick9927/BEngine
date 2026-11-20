using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform.DotNet;
using BEngine.Platform;
using TextAsset=BEngine.Editor.TextAsset;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==Assembly.GetExecutingAssembly().GetName().Name),"Consumer has no Core private grant");
Check(!typeof(AssetTypeRegistry).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==Assembly.GetExecutingAssembly().GetName().Name),"Consumer has no EditorServices private grant");
Check(AssetTypeRegistry.Resolve("A.png")==null && AssetTypeRegistry.Resolve("A.bxml")==null,"Shared registry contains no graphics or UI formats by itself");
var context=new AssetLoadContext(Guid.NewGuid(),"Assets/A.case","source.case","Case","artifact.case");
Check(new TextAsset("文字").text=="文字" && new TextAsset().text=="","Independent source provider can construct the existing editor text representation");
Throws<ArgumentNullException>(()=>new TextAsset(null!),"Text representation rejects null content");
Check(AssetTypeRegistry.LoadFallback(context)==null,"Unknown formats have no implicit recovery");
var primary=0;var recovered=0;AssetLoadContext observed=default;
AssetTypeRegistry.Register<ProbeAsset>("case","Case", c=>{primary++;observed=c;throw new InvalidDataException("bad primary");},null,null,c=>{recovered++;observed=c;return new TextAsset("recovery");});
Check(AssetTypeRegistry.Resolve("A.CASE")=="Case","Suffix matching preserves normalization and case semantics");
Throws<InvalidDataException>(()=>AssetTypeRegistry.Load(context),"Primary errors propagate without implicit recovery");
Check(primary==1 && recovered==0,"Primary load never invokes recovery itself");
Check(AssetTypeRegistry.LoadFallback(context) is TextAsset{ text:"recovery" },"Registered owner can recover to a different base asset kind");
Check(recovered==1 && primary==1 && observed==context,"Recovery forwards exact context and does not rerun primary");
AssetTypeRegistry.Register<ProbeAsset>(".special.case","Special",_=>new ProbeAsset(),null,null,_=>new TextAsset("specific"));
Check(AssetTypeRegistry.LoadFallback(context with{SourcePath="A.SPECIAL.CASE"}) is TextAsset{text:"specific"},"Recovery follows longest matching suffix");
Check(AssetTypeRegistry.Unregister("special.case"),"Explicit unregister removes the selected format");
Check(AssetTypeRegistry.LoadFallback(context with{SourcePath="A.special.case"}) is TextAsset{text:"recovery"},"Unregistered specific suffix reveals the remaining shorter suffix");
AssetTypeRegistry.Register<ProbeAsset>(".case","Replacement",_=>new ProbeAsset());
Check(AssetTypeRegistry.Load(context) is ProbeAsset && AssetTypeRegistry.LoadFallback(context)==null,"Existing overload replaces a registration without retaining old recovery state");
Throws<ArgumentNullException>(()=>AssetTypeRegistry.Register<ProbeAsset>(".case","Invalid",null!,null,null,_=>null),"New overload rejects null primary");
Throws<ArgumentNullException>(()=>AssetTypeRegistry.Register<ProbeAsset>(".case","Invalid",_=>new ProbeAsset(),null,null,null!),"New overload rejects null recovery");
Check(AssetTypeRegistry.Resolve("A.case")=="Replacement","Rejected registration preserves the current declaration");
AssetTypeRegistry.Register<ProbeAsset>(".fault","Fault",_=>new ProbeAsset(),null,null,_=>throw new IOException("recovery failed"));
Throws<IOException>(()=>AssetTypeRegistry.LoadFallback(context with{SourcePath="A.fault"}),"Recovery exceptions propagate without retry or replacement");
AssetTypeRegistry.Register<ProbeAsset>(".none","None",_=>new ProbeAsset(),null,null,_=>null);
Check(AssetTypeRegistry.LoadFallback(context with{SourcePath="A.none"})==null,"A provider may decline recovery");
RuntimeAssemblyCleanup.UnregisterExtensions(Assembly.GetExecutingAssembly());
Check(AssetTypeRegistry.Resolve("A.case")==null && AssetTypeRegistry.LoadFallback(context)==null && AssetTypeRegistry.LoadFallback(context with{SourcePath="A.fault"})==null,"Assembly cleanup releases primary and recovery registrations together");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor"),"Independent registry consumer does not load graphics or workbench");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("ASSET_FORMAT_REGISTRY_OK|"+checks.Count);
public sealed class ProbeAsset:BAsset{}
