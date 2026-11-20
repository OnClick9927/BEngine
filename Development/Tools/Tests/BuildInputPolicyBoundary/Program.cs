using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using BEngine;
using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Editor.Build.ProjectSystem;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Mono.Cecil;
using Mono.Cecil.Cil;

var output=Path.GetFullPath(args[0]);var group=args[1];
var run=Path.Combine(output,group,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(run,"Hub"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
using var scope=EditorServiceScope.Attach(services);
var checks=new List<string>();var matrixCases=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name) where T:Exception {try{action();}catch(TargetInvocationException e) when(e.InnerException is T){Check(true,name);return;}catch(T){Check(true,name);return;}throw new Exception(name+" did not throw");}
string Text(string name,string text){var path=Path.Combine(run,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);return path;}
Assembly Module(string name){var a=Assembly.Load(name);RuntimeHelpers.RunModuleConstructor(a.ManifestModule.ModuleHandle);return a;}
string Image(string name,string? binding=null,bool nested=false,bool forwarder=false,bool stale=false,params string[] literals)
{
 var file=Path.Combine(run,name+"-"+Guid.NewGuid().ToString("N")+".dll");
 using var a=AssemblyDefinition.CreateAssembly(new(name,new Version(1,0)),name,ModuleKind.Dll);
 var t=new TypeDefinition("Probe","Outer",Mono.Cecil.TypeAttributes.Public,a.MainModule.TypeSystem.Object);a.MainModule.Types.Add(t);
 var m=new MethodDefinition("Run",Mono.Cecil.MethodAttributes.Public|Mono.Cecil.MethodAttributes.Static,a.MainModule.TypeSystem.Void);t.Methods.Add(m);
 foreach(var literal in literals){m.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr,literal));m.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));}m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
 if(binding is not null)
 {
  var reference=new AssemblyNameReference(binding,new Version(1,0));a.MainModule.AssemblyReferences.Add(reference);
  if(forwarder)
  {
   var e=new ExportedType("SkiaSharp","Canvas",a.MainModule,reference){Attributes=Mono.Cecil.TypeAttributes.Public|Mono.Cecil.TypeAttributes.Forwarder};a.MainModule.ExportedTypes.Add(e);
   if(nested)a.MainModule.ExportedTypes.Add(new ExportedType("","Nested",a.MainModule,reference){DeclaringType=e,Attributes=Mono.Cecil.TypeAttributes.NestedPublic|Mono.Cecil.TypeAttributes.Forwarder});
  }
  else if(!stale)
  {
   var type=new TypeReference(binding,"Canvas",a.MainModule,reference);
   if(nested)type=new TypeReference("","Nested",a.MainModule,reference){DeclaringType=type};
   t.Fields.Add(new("Binding",Mono.Cecil.FieldAttributes.Public,type));
  }
 }
 a.Write(file);return file;
}
RuntimePackageResourceInput Resource(string name,string text)
{
 var file=Text(name,text);return new("probe","Assets/Packages/probe/"+name,file,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))),new FileInfo(file).Length);
}
var corePlayer=typeof(PlayerManagedCodeBuildPipeline).Assembly;
var trimmer=corePlayer.GetType("BEngine.Editor.Build.PlayerManagedCodeTrimmer",true)!;
HashSet<string> AssetTypes(string path)
{
 var types=new HashSet<string>(StringComparer.Ordinal);
 trimmer.GetMethod("CollectAssetTypeNames",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[path,types]);return types;
}
if(group=="Core")
{
 var buildTypes=new[]{typeof(BuildArchitecture),typeof(BuildArtifactKey),typeof(BuildPlatformDescriptor),
  typeof(BuildTargetDescriptor),typeof(BuildTargetPlatform),typeof(ManagedCodeRuntimeKind),
  typeof(BEngine.Rendering.Rhi.GraphicsBackend)};
 Check(buildTypes.All(type=>type.Assembly.GetName().Name=="BEngine.Core"&&
  Type.GetType($"{type.FullName}, BEngine.Core")==type),
  "Build descriptors and artifact keys move to BuildCore with old assembly type names intact");
 Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Graphics.Editor" or "BEngine.UIElements.Editor" or "BEngine.AssetBundles.Editor"),"Core format checks run without functional editor packages");
 Check(new[]{".yaml",".yml",".json",".xml",".txt"}.All(e=>PlayerResourceTextFormats.IsTextFile("root/DATA"+e.ToUpperInvariant())),"Generic document formats remain available without packages");
 Check(new[]{".shader",".glsl",".bxml",".bss",".bcss",".bin",".png"}.All(e=>!PlayerResourceTextFormats.IsTextFile("root/data"+e)),"Core does not own graphics, UI or binary formats");
 Check(!PlayerResourceTextFormats.IsTextFile("folder.json/data.bin"),"Directory suffixes do not classify a binary file");
 Check(!PlayerResourceTextFormats.GetReferencedTypeNames("data.xml","<Root><Control/></Root>").Any(),"Core XML handling does not interpret UI tags");
 var before=PlayerResourceReferenceSet.Read([],[Text("ignored.probe","Assets/ignored.png")]);
 Check(!before.ReferencesAssetPath("Assets/ignored.png"),"Unregistered content is not read as a generic text file");
 var formats=new[]{".probe"};PlayerResourceTextFormats.Register(formats);formats[0]=".modified";
 Check(PlayerResourceTextFormats.IsTextFile("a.PROBE")&&!PlayerResourceTextFormats.IsTextFile("a.modified"),"Format registration copies inputs and compares extensions without case");
 Check(PlayerResourceReferenceSet.Read([],[Text("selected.probe","Assets/kept.png")]).ReferencesAssetPath("Assets/kept.png"),"Resource discovery consumes contributed text formats");
 Check(!before.ReferencesAssetPath("Assets/ignored.png"),"A previously captured reference set stays immutable after registration");
 PlayerResourceTextFormats.Register((_,_)=>new[]{"Probe.Control"},".custom");
 Check(!PlayerResourceTextFormats.IsTextFile("a.probe")&&PlayerResourceTextFormats.IsTextFile("a.custom"),"An owner replaces its previous contribution");
 var pending=PlayerResourceTextFormats.GetReferencedTypeNames("a.custom","");
 Check(pending.SequenceEqual(new[]{"Probe.Control"}),"A custom format contributes runtime type names without constructing objects");
 Check(AssetTypes(Text("types.custom","plain words")).SetEquals(new[]{"Probe.Control"}),"The actual managed trimmer consumes the custom format reader");
 Check(!PlayerResourceTextFormats.GetReferencedTypeNames("a.bin","").Any(),"A custom type reader is restricted to its declared extensions");
 foreach(var invalid in new[]{"", ".", "custom", ".a.b", ".a/b", ".a\\b", ".a b"})
  Throws<ArgumentException>(()=>PlayerResourceTextFormats.Register(".valid",invalid),"Invalid extension is rejected atomically: "+invalid);
 Check(PlayerResourceTextFormats.IsTextFile("a.custom")&&!PlayerResourceTextFormats.IsTextFile("a.valid"),"Failed registration leaves the prior owner intact");
 Throws<ArgumentNullException>(()=>PlayerResourceTextFormats.Register((string[])null!),"Null format arrays are rejected");
 Throws<ArgumentNullException>(()=>PlayerResourceTextFormats.Register((Func<string,string,IEnumerable<string>>)null!,".probe"),"Null type readers are rejected");
 var root=Resource("root.custom","child.custom");var child=Resource("child.custom","grand.txt");var grand=Resource("grand.txt","");
 var selected=PlayerResourceReferenceSet.Read([]).SelectResources([grand,child,root],[],new(),r=>r==root);
 Check(selected.SequenceEqual(new[]{grand,child,root}),"Resource closure follows contributed text and preserves input order");
 RuntimeAssemblyCleanup.UnregisterExtensions(Assembly.GetExecutingAssembly());
 Check(!PlayerResourceTextFormats.IsTextFile("a.custom")&&!PlayerResourceTextFormats.GetReferencedTypeNames("a.custom","").Any(),"Assembly cleanup removes its formats and type reader");
 Check(PlayerResourceTextFormats.IsTextFile("a.xml"),"Cleanup preserves generic documents");
 Check(pending.SequenceEqual(new[]{"Probe.Control"}),"A query captures its contribution snapshot rather than mutable registry state");
 Check(PlayerResourceReferenceSet.Read([]).SelectResources([grand,child,root],[],new(),r=>r==root).SequenceEqual(new[]{root}),"New builds do not retain a removed format reader");
 PlayerResourceTextFormats.Register();
 Parallel.For(0,64,i=>{PlayerResourceTextFormats.Register(i%2==0?".first":".second");_ = PlayerResourceTextFormats.IsTextFile("a.first");});
 Check(PlayerResourceTextFormats.IsTextFile("a.first")^PlayerResourceTextFormats.IsTextFile("a.second"),"Concurrent build-time registration publishes complete snapshots");
 RuntimeAssemblyCleanup.UnregisterExtensions(Assembly.GetExecutingAssembly());
}
else if(group=="Formats")
{
 var graphics=Module("BEngine.Graphics.Editor");var ui=Module("BEngine.UIElements.Editor");
 Check(new[]{".shader",".glsl",".glslinc",".hlsl",".hlslinc",".cg",".wgsl",".msl",".metal"}.All(e=>PlayerResourceTextFormats.IsTextFile("asset"+e)),"Graphics owns shader and include text formats");
 Check(new[]{".bxml",".bss",".bcss"}.All(e=>PlayerResourceTextFormats.IsTextFile("asset"+e.ToUpperInvariant())),"UIElements owns its document and style formats");
 var xml="<ui:Root xmlns:ui='clr-namespace:Game.UI;assembly=Game'><ui:Custom/><Button/><raw:Data xmlns:raw='https://example.invalid/schema'/></ui:Root>";
 var names=PlayerResourceTextFormats.GetReferencedTypeNames("screen.bxml",xml).ToHashSet();
 Check(names.SetEquals(new[]{"Root","Game.UI.Root","Custom","Game.UI.Custom","Button","Data"}),"UI type discovery preserves local, CLR namespace and unqualified tag names");
 Check(PlayerResourceTextFormats.GetReferencedTypeNames("screen.xml",xml).ToHashSet().SetEquals(names),"Existing XML UI documents retain the same type discovery");
 Check(!PlayerResourceTextFormats.GetReferencedTypeNames("screen.bss","not XML").Any(),"UI styles do not pass through the document parser");
 Check(AssetTypes(Text("screen.bxml",xml)).IsSupersetOf(names),"Managed trimming uses the actual UIElements document reader");
 Throws<XmlException>(()=>PlayerResourceTextFormats.GetReferencedTypeNames("broken.bxml","<Root>").ToArray(),"Malformed UI documents still fail validation");
 Throws<XmlException>(()=>PlayerResourceTextFormats.GetReferencedTypeNames("dtd.bxml","<!DOCTYPE x [<!ENTITY xxe SYSTEM 'file:///missing'>]><Root>&xxe;</Root>").ToArray(),"UI document parsing prohibits DTD and external entities");
 foreach(var ext in new[]{".shader",".glsl",".glslinc",".hlsl",".hlslinc",".bxml",".bss",".bcss"})
 {
  var file=Text("referenced"+ext,"Assets/kept/"+ext[1..]+".png");
  Check(PlayerResourceReferenceSet.Read([],[file]).ReferencesAssetPath("Assets/kept/"+ext[1..]+".png"),"Original format still contributes resource references: "+ext);
 }
 foreach(var ext in new[]{".wgsl",".msl",".metal",".cg"})
  Check(PlayerResourceReferenceSet.Read([],[Text("extra"+ext,"Assets/additional.png")]).ReferencesAssetPath("Assets/additional.png"),"Graphics textual backend references are supported: "+ext);
 RuntimeAssemblyCleanup.UnregisterExtensions(graphics);
 Check(!PlayerResourceTextFormats.IsTextFile("a.glsl")&&PlayerResourceTextFormats.IsTextFile("a.bxml"),"Graphics cleanup removes only its formats");
 RuntimeAssemblyCleanup.UnregisterExtensions(ui);
 Check(!PlayerResourceTextFormats.IsTextFile("a.bxml")&&PlayerResourceTextFormats.IsTextFile("a.xml"),"UI cleanup removes its formats while preserving generic XML");
 Check(!PlayerResourceTextFormats.GetReferencedTypeNames("a.xml",xml).Any(),"UI cleanup removes its XML type semantics");
}
else if(group=="Platform")
{
 var compact=PlatformTextBuildPolicy.For(BuildTargetPlatform.Web,WebFontBackend.FreeType);
 var standard=PlatformTextBuildPolicy.For(BuildTargetPlatform.Web,WebFontBackend.Skia);
 foreach(var platform in Enum.GetValues<BuildTargetPlatform>())
 foreach(var backend in Enum.GetValues<WebFontBackend>())
 {
  var policy=PlatformTextBuildPolicy.For(platform,backend);var free=platform==BuildTargetPlatform.Web&&backend==WebFontBackend.FreeType;
  Check(policy.GetDefineSymbols(new[]{"Z","BENGINE_WEB_FONTS","A","A"}).SequenceEqual(free?new[]{"A","BENGINE_WEB_FONTS","Z"}:new[]{"A","Z"}),"Target-specific symbols: "+platform+"/"+backend);
  Check(policy.IncludesBinding("SkiaSharp")==!free&&policy.IncludesBinding("SkiaSharp.HarfBuzz")==!free&&policy.IncludesBinding("HarfBuzzSharp"),"Target-specific binding selection: "+platform+"/"+backend);
  matrixCases++;
 }
 Check(!compact.IncludesBinding("skiasharp")&&compact.IncludesBinding("Game.SkiaSharpAdapter"),"Only exact excluded binding identities are removed");
 Check(!compact.IncludesBinding("HarfBuzzSharp",false)&&!standard.IncludesBinding("SkiaSharp",false)&&standard.IncludesBinding("Game",false),"Disabling native text removes only its platform bindings");
 Check(PlatformTextBuildPolicy.BindingAnchors.Count==3&&PlatformTextBuildPolicy.BindingAnchors["SkiaSharp"]=="SkiaSharp.SKFont", "Platform owns the native text registration anchors");
 Check(PlatformTextBuildPolicy.FindReflectedTypes("SkiaSharp.HarfBuzz.SKShaper, SkiaSharp.HarfBuzz; HarfBuzzSharp.Blob; SkiaSharp.Container+Nested`1").Select(t=>t.AssemblyName+":"+t.TypeName).Contains("SkiaSharp:SkiaSharp.Container+Nested`1"),"Reflection policy handles nested and generic native binding types");
 Check(PlatformTextBuildPolicy.FindReflectedTypes("HarfBuzzSharp.Blob").Single()==("HarfBuzzSharp","HarfBuzzSharp.Blob"),"Reflection types retain their actual binding owner");
 Check(!PlatformTextBuildPolicy.FindReflectedTypes("Game.Widget").Any()&&PlatformTextBuildPolicy.ReferencesBindings("SkiaSharp"),"Binding literal detection does not invent other reflection targets");
 foreach(var binding in new[]{"SkiaSharp","SkiaSharp.HarfBuzz"})
 foreach(var nested in new[]{false,true})
 foreach(var forwarder in new[]{false,true})
 {
  var path=Image("Contract",binding,nested,forwarder);
  Throws<InvalidDataException>(()=>compact.ValidateRuntimeContracts([path]),"Compact Web rejects surviving binding contracts: "+binding+"/"+nested+"/"+forwarder);
  standard.ValidateRuntimeContracts([path]);matrixCases++;
 }
 compact.ValidateRuntimeContracts([Image("Stale","SkiaSharp",stale:true),Image("Admitted","HarfBuzzSharp")]);
 Check(true,"Unused AssemblyRef rows and admitted HarfBuzz contracts do not reject compact Web");
 Check(!File.Exists(Path.Combine(run,"missing.dll")),"Missing-image fixture is absent");
 standard.ValidateRuntimeContracts([Path.Combine(run,"missing.dll")]);
 Throws<FileNotFoundException>(()=>compact.ValidateRuntimeContracts([Path.Combine(run,"missing.dll")]),"Active validation reports missing images");
 var rootBuilder=corePlayer.GetType("BEngine.Editor.Build.PlayerHostContractRoots",true)!;
 var requires=rootBuilder.GetMethod("RequiresTextBindings",BindingFlags.Static|BindingFlags.NonPublic)!;
 bool Requires(string[] images,string?[]? descriptors=null)=>(bool)requires.Invoke(null,[images,descriptors??[]])!;
 Check(!Requires([Image("NoText","SkiaSharp",stale:true)]),"Core host analysis does not treat stale AssemblyRef rows as text usage");
 Check(Requires([Image("TextUse","SkiaSharp")]),"Core host analysis consumes Platform binding classification");
 Check(Requires([Image("ReflectionUse",literals:["SkiaSharp.SKFont, SkiaSharp"])]),"Core host analysis consumes Platform reflection-literal policy");
 Check(Requires([],[Text("roots.xml","<linker><assembly fullname='HarfBuzzSharp, Version=1.0.0.0'/></linker>")]),"Explicit reflection descriptors retain native text bindings");
 Check(!Requires([],[Text("other.xml","<linker><assembly fullname='Game'/></linker>")]),"Unrelated descriptors do not retain native text bindings");
}
else throw new ArgumentException(group);
var result=new{Passed=true,Checks=checks.Count,MatrixCases=matrixCases,Details=checks};
Directory.CreateDirectory(Path.Combine(output,group));File.WriteAllText(Path.Combine(output,group,"Result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(result));
