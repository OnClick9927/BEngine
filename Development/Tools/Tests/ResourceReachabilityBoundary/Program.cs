using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Editor.Build.ProjectSystem;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
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
string Text(string name,string value){var path=Path.Combine(run,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,value);return path;}
string Image(params string[] strings)
{
 var file=Path.Combine(run,Guid.NewGuid().ToString("N")+".dll");
 using var a=AssemblyDefinition.CreateAssembly(new("ReferenceProbe",new Version(1,0)),"ReferenceProbe",ModuleKind.Dll);
 var t=new TypeDefinition("Probe","Outer",Mono.Cecil.TypeAttributes.Public,a.MainModule.TypeSystem.Object);a.MainModule.Types.Add(t);
 var nested=new TypeDefinition("","Nested",Mono.Cecil.TypeAttributes.NestedPublic,a.MainModule.TypeSystem.Object);t.NestedTypes.Add(nested);
 var method=new MethodDefinition("References",Mono.Cecil.MethodAttributes.Public|Mono.Cecil.MethodAttributes.Static,a.MainModule.TypeSystem.Void);nested.Methods.Add(method);
 foreach(var value in strings){method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr,value));method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));}
 method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));a.Write(file);return file;
}
RuntimePackageResourceInput Resource(string name,string text)
{
 var file=Text(name,text);return new("probe","Assets/Packages/probe/"+name.Replace('\\','/'),file,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))),new FileInfo(file).Length);
}
string Project()
{
 var path=Path.Combine(run,"Project");Directory.CreateDirectory(Path.Combine(path,"ProjectSettings"));Directory.CreateDirectory(Path.Combine(path,"Assets"));
 File.WriteAllText(Path.Combine(path,"Project.yaml"),"format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Resource Test\n");return path;
}
PlayerBuildContext Context(string path)=>new(new PlayerBuildRequest{ProjectPath=path,TargetId="windows"},BuildTargetUtility.Current.Get("windows"),Path.Combine(run,"Staging"),null);
if(group=="Core")
{
 var guid=Guid.NewGuid();var other=Guid.NewGuid();
 var image=Image("Assets\\CustomStartup\\hero.png","cfg/table","Assets/CustomStartup/Folder/","guid:"+guid.ToString("D")+"#42");
 var json=Text("selected.json","Assets/CustomStartup/text.png "+other.ToString("N"));
 var ignored=Text("ignored.bin","Assets/CustomStartup/ignored.png");
 var refs=PlayerResourceReferenceSet.Read([image,image],[json,ignored]);
 Check(refs.ReferencesAssetPath("assets/customstartup/HERO.png"),"Real nested IL strings normalize separators and compare addresses without case");
 Check(refs.ReferencesAssetPath("Assets/CustomStartup/text.png"),"Selected text contributes full project addresses");
 Check(!refs.ReferencesAssetPath("Assets/CustomStartup/ignored.png"),"Binary input is not decoded as text");
 Check(refs.ReferencesAssetPath("Assets/Any/table.yaml","cfg/table.data.yaml"),"A provider alias supports the original first-dot stem convention");
 Check(refs.ReferencesAssetPath("Assets/CustomStartup/Folder/child.png",directoryLiteralPrefix:"Assets/CustomStartup/"),"Content providers choose the directory-literal scope");
 Check(!refs.ReferencesAssetPath("Assets/CustomStartup/Folder/child.png",directoryLiteralPrefix:"Assets/AOT/"),"A different content partition does not inherit those directory roots");
 Check(!refs.ReferencesAssetPath("Assets/CustomStartup/Folder2/child.png",directoryLiteralPrefix:"Assets/CustomStartup/"),"Directory references respect path segment boundaries");
 Check(refs.ReferencesGuid(guid),"guid-prefixed subasset literals retain the owning GUID");
 Check(refs.ReferencesGuid(other),"GUIDs in selected text retain matching assets");
 Check(!refs.ReferencesGuid(Guid.NewGuid()),"Unreferenced GUIDs remain absent");
 File.WriteAllText(json,"Assets/CustomStartup/new.png");
 Check(refs.ReferencesAssetPath("Assets/CustomStartup/text.png")&&!refs.ReferencesAssetPath("Assets/CustomStartup/new.png"),"A reference snapshot is immutable after source text changes");
 Check(PlayerResourceReferenceSet.Read([],[json]).ReferencesAssetPath("Assets/CustomStartup/new.png"),"A new build reads current content without a global stale cache");
 var a=Resource("cycle/a.yaml","b.yaml");var b=Resource("cycle/b.yaml","c.yaml");var c=Resource("cycle/c.yaml","a.yaml");var dead=Resource("dead.yaml","a.yaml");
 var empty=PlayerResourceReferenceSet.Read([]);var report=new PlayerStrippingReport();
 var selected=empty.SelectResources([c,b,a,dead],[],report,r=>r==a);
 Check(selected.SequenceEqual([c,b,a]),"A reverse-ordered three-resource cycle reaches a stable closure and keeps input order");
 Check(report.Resources.Count==4&&report.Resources.Count(r=>r.Included)==3,"The report includes each live and dead resource exactly once");
 Check(report.Resources.Where(r=>r.Included).All(r=>r.OriginalBytes==r.PublishedBytes)&&report.Resources.Single(r=>!r.Included).PublishedBytes==0,"Published sizes follow selection");
 Check(empty.SelectResources([a,b,c,dead],[],new()).Count==0,"Selection does not leak discovered texts into a reused snapshot");
 Check(PlayerResourceReferenceSet.Read([Image("a.yaml")]).SelectResources([c,b,a,dead],[],new()).SequenceEqual([c,b,a]),"IL literals seed the same transitive resource closure");
 var preserved=Resource("family/icon.bin","");var sibling=Resource("family2/icon.bin","");
 Check(empty.SelectResources([preserved,sibling],["assets/packages/probe/FAMILY"],new()).SequenceEqual([preserved]),"Explicit preservation is case-insensitive and excludes sibling prefixes");
 Throws<InvalidDataException>(()=>empty.SelectResources([a],["Assets/Packages/probe/absent"],new()),"Missing explicit resources fail instead of silently disappearing");
 var name=Resource("program.vulkan.vert.glsl","");
 Check(PlayerResourceReferenceSet.Read([Image("program")]).SelectResources([name],[],new()).Count==1,"Program-name literals retain resource families");
 var nontext=Resource("opaque.bin","c.yaml");
 Check(empty.SelectResources([nontext,c],[],new(),r=>r==nontext).SequenceEqual([nontext]),"Retained binary resources do not create false text references");
 using var cancellation=new CancellationTokenSource();cancellation.Cancel();var touched=false;var cancelledReport=new PlayerStrippingReport();
 Throws<OperationCanceledException>(()=>PlayerResourceReferenceSet.Read(Exploding(),cancellationToken:cancellation.Token),"Cancellation precedes assembly enumeration");
 Throws<OperationCanceledException>(()=>empty.SelectResources([a],[],cancelledReport,r=>{touched=true;return true;},cancellation.Token),"Cancellation precedes root callbacks");
 Check(!touched&&cancelledReport.Resources.Count==0,"A cancelled selection does not modify the report");
 Throws<FileNotFoundException>(()=>PlayerResourceReferenceSet.Read([Path.Combine(run,"absent.dll")]),"Missing assembly input is reported");
 Throws<FileNotFoundException>(()=>PlayerResourceReferenceSet.Read([],[Path.Combine(run,"absent.yaml")]),"Missing selected text is reported");
 var project=Project();var context=Context(project);context.StrippingReport=new();
 var settings=Path.Combine(project,"ProjectSettings/PlayerStripping.yaml");
 File.WriteAllText(settings,"format: BEngine.PlayerStripping\nversion: 1\npreservedAssets:\n- Assets/CustomStartup/hero.png\n");
 Check(PlayerResourceBuildPipeline.SelectHostResources(context,[]).Count==0,"Core accepts a custom content provider's project-address scope");
 File.WriteAllText(settings,"format: BEngine.PlayerStripping\nversion: 1\npreservedAssets:\n- Assets/AOT/../outside\n");
 Throws<InvalidDataException>(()=>PlayerResourceBuildPipeline.SelectHostResources(context,[]),"Core still rejects traversal in preservation settings");
 File.Delete(settings);
 var host=Resource("bootstrap.txt","") with {Address="Assets/Resources/probe/bootstrap.txt"};
 Check(PlayerResourceBuildPipeline.SelectHostResources(context,[host],r=>r==host).Count==1,"An explicit provider root participates in host reachability");
 Check(ShaderBuildPipeline.IsResourceSupported("unknown.custom",[GraphicsBackend.WebGL])&&!ShaderBuildPipeline.IsRequiredBootstrapResource("Assets/Resources/unknown.custom"),"Without a graphics provider Core invents no shader convention");
 RuntimeServiceContributions.RegisterDefault<IShaderResourceBuildPolicy>(new CustomPolicy());
 Check(!ShaderBuildPipeline.IsResourceSupported("blocked.custom",[GraphicsBackend.WebGL])&&ShaderBuildPipeline.IsRequiredBootstrapResource("custom-root"),"A non-graphics contributor can implement the neutral resource policy");
 RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
 Check(ShaderBuildPipeline.IsResourceSupported("blocked.custom",[GraphicsBackend.WebGL])&&!ShaderBuildPipeline.IsRequiredBootstrapResource("custom-root"),"Unregistering a policy owner releases its resource rules");
 Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.AssetBundles" or "BEngine.AssetBundles.Editor" or "BEngine.Graphics" or "BEngine.Graphics.Editor" or "BEngine.IMGUI.Editor"),"Core resource analysis runs without content or graphics packages");
 IEnumerable<string> Exploding(){yield return Fail();}
 string Fail()=>throw new Exception("Must not enumerate");
}
else if(group=="Integration")
{
 var graphics=Assembly.Load("BEngine.Graphics.Editor");RuntimeHelpers.RunModuleConstructor(graphics.ManifestModule.ModuleHandle);
 Check(RuntimeServiceContributions.GetDefault<IShaderResourceBuildPolicy>()?.GetType().Assembly==graphics,"Graphics owns and registers the actual shader resource policy");
 var expected=new Dictionary<string,GraphicsBackend[]>{
  ["direct3d11"]=[GraphicsBackend.Direct3D11],["direct3d12"]=[GraphicsBackend.Direct3D12],
  ["direct3d"]=[GraphicsBackend.Direct3D11,GraphicsBackend.Direct3D12],["vulkan"]=[GraphicsBackend.Vulkan],
  ["spirv"]=[GraphicsBackend.Vulkan],["opengles"]=[GraphicsBackend.OpenGLES],
  ["opengl"]=[GraphicsBackend.OpenGL,GraphicsBackend.OpenGLES,GraphicsBackend.WebGL],
  ["webgpu"]=[GraphicsBackend.WebGPU],["webgl"]=[GraphicsBackend.WebGL],["metal"]=[GraphicsBackend.Metal]};
 foreach(var backend in Enum.GetValues<GraphicsBackend>().Where(b=>b!=GraphicsBackend.Auto))
 {
  var valid=true;
  foreach(var rule in expected)foreach(var suffix in new[]{"wgsl","hlsl","glsl","spv","msl","metal"})
  {matrixCases++;valid&=ShaderBuildPipeline.IsResourceSupported("root/name."+rule.Key+".vert."+suffix,[backend])==rule.Value.Contains(backend);}
  Check(valid,"All shader tags and extensions match the supported backend matrix for "+backend);
 }
 Check(ShaderBuildPipeline.IsResourceSupported("x.VULKAN.FRAG.GLSL",[GraphicsBackend.Vulkan]),"Shader classification preserves case-insensitive tags");
 Check(ShaderBuildPipeline.IsResourceSupported("C:\\Shaders\\name.vulkan.vert.glsl",[GraphicsBackend.Vulkan]),"Windows separators work in shader policy");
 Check(!ShaderBuildPipeline.IsResourceSupported("/folder.vulkan/name.webgl.vert.glsl",[GraphicsBackend.Vulkan]),"Directory names do not masquerade as file backend tags");
 Check(ShaderBuildPipeline.IsResourceSupported("name.webgl.png",[GraphicsBackend.Vulkan]),"Ordinary resources are not removed because their names contain backend tags");
 Check(ShaderBuildPipeline.IsResourceSupported("untagged.shader",[GraphicsBackend.WebGL]),"Unclassified resources remain provider-independent");
 Check(ShaderBuildPipeline.IsResourceSupported("name.webgl.vert.glsl",[GraphicsBackend.Vulkan,GraphicsBackend.WebGL]),"Multiple concrete backends retain the union");
 Check(ShaderBuildPipeline.IsRequiredBootstrapResource("Assets/Resources/Shaders/Common/core.glsl"),"Graphics retains its common bootstrap resources");
 Check(!ShaderBuildPipeline.IsRequiredBootstrapResource("Assets/Resources/Other/core.glsl"),"Other host resources are not blanket roots");
 RuntimeServiceContributions.RegisterDefault<IShaderResourceBuildPolicy>(new CustomPolicy());
 Check(!ShaderBuildPipeline.IsResourceSupported("blocked.custom",[GraphicsBackend.WebGL]),"A replacement policy is used without scanning packages");
 RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
 Check(ShaderBuildPipeline.IsRequiredBootstrapResource("Assets/Resources/Shaders/Common/core.glsl"),"Removing a replacement restores Graphics policy ownership");
 var ab=Assembly.Load("BEngine.AssetBundles.Editor");
 var stripper=ab.GetType("BEngine.Editor.Build.PlayerResourceStripper",true)!;
 var rootsMethod=stripper.GetMethod("GetProjectRoots",BindingFlags.Static|BindingFlags.NonPublic)!;
 string[] Roots(string[] assets,string[] assemblies,PlayerStrippingSettings settings,AssetRecord[]? records=null,string[]? texts=null)=>(string[])rootsMethod.Invoke(null,new object?[]{Array.Empty<string>(),assets,assemblies,settings,records,texts})!;
 var guid=Guid.NewGuid();var record=new AssetRecord(guid,"Assets/AOT/guid.png","source","meta","artifact","Texture","hash",false);
 var image=Image("Assets/AOT/folder/","guid:"+guid.ToString("N")+"#7");
 var candidates=new[]{record.AssetPath,"Assets/AOT/folder/child.png","Assets/AOT/Resources/data.yaml","Assets/AOT/unreferenced.png"};
 var roots=Roots(candidates,[image],new(),[record]);
 Check(roots.SequenceEqual(new[]{"Assets/AOT/Resources/data.yaml",record.AssetPath,"Assets/AOT/folder/child.png"}.Order(StringComparer.Ordinal)),"AB composes Core references with its Resources promise, AOT directories and asset GUIDs");
 Check(!roots.Contains("Assets/AOT/unreferenced.png"),"AB does not root unrelated candidates");
 Throws<InvalidDataException>(()=>Roots(candidates,[],new(){PreservedAssets=["Assets/CustomStartup/hero.png"]}),"AB validates its own partition scope after Core becomes content-neutral");
 Throws<InvalidDataException>(()=>Roots(candidates,[],new(){PreservedAssets=["Assets/AOT/missing.png"]}),"AB still rejects missing explicitly preserved project assets");
 var compat=ab.GetType("BEngine.Editor.Build.PlayerPackagedResourceArchiveBuilder",true)!.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="IsShaderResourceSupported"&&m.GetParameters().Length==2);
 Check(!(bool)compat.Invoke(null,["shader.vulkan.vert.glsl",new[]{GraphicsBackend.WebGL}])!,"AB's existing shader entry delegates to the registered Graphics policy");
 var filter=stripper.GetMethod("FilterPackageResources",BindingFlags.Static|BindingFlags.NonPublic)!;
 var resource=Resource("a.yaml","");var inputs=new RuntimeManagedCodeReleaseInputSet([], [resource], [run]);
 var selectedText=Text("scene.yaml",resource.Address);var report=new PlayerStrippingReport();
 var selected=(RuntimeManagedCodeReleaseInputSet)filter.Invoke(null,[inputs,new[]{selectedText},new PlayerStrippingSettings(),report,false,null])!;
 Check(selected.PackageResources.SequenceEqual([resource])&&ReferenceEquals(selected.Assemblies,inputs.Assemblies),"AB consumes Core's resource selection without replacing code images");
 Check(selected.PackageResourceRoots.SequenceEqual([run])&&report.Resources.Single().Included,"AB retains resource roots and reachability reporting");
 var project=Project();var context=Context(project);context.StrippingReport=new();
 var common=Resource("common.glsl","") with {Address="Assets/Resources/Shaders/Common/common.glsl"};
 var dead=Resource("unused.bin","") with {Address="Assets/Resources/Other/unused.bin"};
 Check(PlayerResourceBuildPipeline.SelectHostResources(context,[common,dead],r=>ShaderBuildPipeline.IsRequiredBootstrapResource(r.Address)).SequenceEqual([common]),"Host selection combines Core code analysis with Graphics bootstrap roots");
 RuntimeServiceContributions.UnregisterAssembly(graphics);
 Check(RuntimeServiceContributions.GetDefault<IShaderResourceBuildPolicy>() is null&&!ShaderBuildPipeline.IsRequiredBootstrapResource(common.Address),"Graphics unload removes its policy and no stale bootstrap rule survives");
}
else throw new ArgumentException(group);
var result=new{Passed=true,Checks=checks.Count,ShaderMatrixCases=matrixCases,Details=checks};
Directory.CreateDirectory(Path.Combine(output,group));File.WriteAllText(Path.Combine(output,group,"Result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(result));
sealed class CustomPolicy:IShaderResourceBuildPolicy
{
 public bool IsSupported(string filePath,IReadOnlyCollection<GraphicsBackend> backends)=>filePath!="blocked.custom";
 public bool IsRequiredBootstrapResource(string address)=>address=="custom-root";
}
