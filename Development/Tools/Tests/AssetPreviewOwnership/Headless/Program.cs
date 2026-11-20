using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;
using static BEngine.Editor.EditorAssetPreviewSources;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);
BEngine.Platform.DotNet.DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var scope=EditorServiceScope.Attach(services);
RuntimeHelpers.RunModuleConstructor(typeof(TextureImporter).Module.ModuleHandle);
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
var texture=new Texture(16,24);var provider=EditorAssetAdapters.GetPreview(texture)!;
Check(provider.GetType().Assembly==typeof(TextureImporter).Assembly,"Graphics owns texture preview policy");
Check(provider.GetInfo(texture)==$"16 x 24 | {texture.compressionFormat}","Texture metadata remains exact");
Check(provider.GetContent(texture).Kind==EditorAssetPreviewKind.Unavailable,"Missing texture uses its domain unavailable state");
var shader=Shader.Find("Fixture/Preview");var material=new Material(shader){color=new Color(Fix64.Half,0,1,1)};
var materialPreview=EditorAssetAdapters.GetPreview(material)!;
Check(materialPreview.GetContent(material).Color.Equals(material.color)&&materialPreview.GetContent(material).Kind==EditorAssetPreviewKind.ColorSample,"Material policy returns its exact color without GUI");
Check(EditorAssetAdapters.GetPreview(shader)!.GetContent(shader).Title==shader.shaderName,"Shader metadata belongs to Graphics");
var font=(Font)Activator.CreateInstance(typeof(Font),true)!;
Check(EditorAssetAdapters.GetPreview(font)!.GetInfo(font)==$"Font | Default size {font.defaultSize} | {font.characterSet}","Font metadata remains available without IMGUI");
var generic=new PlainAsset();var child=new ChildAsset();var oldProvider=EditorAssetAdapters.GetPreview(child);
Check(oldProvider?.GetType().Assembly==typeof(EditorAssetPreviewSources).Assembly,"Unknown assets use shared generic authoring preview");
var first=new TestPreview();EditorAssetAdapters.Register(new Adapter(typeof(PlainAsset),first));
Check(ReferenceEquals(EditorAssetAdapters.GetPreview(child),first),"Registration invalidates cached base candidates");
var exact=new TestPreview();EditorAssetAdapters.Register(new Adapter(typeof(ChildAsset),exact));
Check(ReferenceEquals(EditorAssetAdapters.GetPreview(child),exact),"Exact preview wins over inherited preview");
exact.Enabled=false;
Check(ReferenceEquals(EditorAssetAdapters.GetPreview(child),first),"Source eligibility is reevaluated without rebuilding candidates");
var replacement=new TestPreview();EditorAssetAdapters.Register(new Adapter(typeof(ChildAsset),replacement));
Check(ReferenceEquals(EditorAssetAdapters.GetPreview(child),replacement),"Same-assembly registration replacement invalidates cached candidates");
Check(!EditorAssetAdapters.IsReadOnly(child),"New presentation does not alter existing read-only semantics");
var png=Path.Combine(output,"sample.png");File.WriteAllBytes(png,[1,2,3,4]);
var codec=new TestCodec();var saved=EncodedImages.Current;EncodedImages.Current=codec;ClearTemporaryAssetPreviews();
var changes=new List<AssetPreviewCacheInvalidation>();Action<AssetPreviewCacheInvalidation> callback=changes.Add;Invalidated+=callback;
try
{
 var one=GetImage(png);var two=GetImage(png);
 Check(one is {Width:2,Height:3}&&one==two&&codec.Reads==1,"Repeated unchanged preview source decodes metadata once");
 Check(one!.Value.Source.StartsWith(png+PreviewRevisionQuery,StringComparison.Ordinal),"Revision URL retains file identity and stamp");
 File.WriteAllBytes(png,[1,2,3,4,5]);var next=GetImage(png);
 Check(next!=one&&codec.Reads==2,"Length changes invalidate decoded metadata and revision identity");
 Invalidate(png);_=GetImage(png);
 Check(codec.Reads==3&&changes[^1].Path==png,"Explicit invalidation notifies consumers and drops source metadata");
 codec.Valid=false;File.WriteAllBytes(png,[1,2,3]);
 Check(GetImage(png)==null&&changes[^1].Path==png,"A source becoming unsupported invalidates its previous GPU resource");
 var reads=codec.Reads;_=GetImage(png);Check(codec.Reads==reads,"Unavailable preview results are cached by source stamp");
 EncodedImages.Current=null;Check(GetImage(png)==null&&changes[^1].Path==null,"Decoder removal invalidates the global image cache");
 var nextCodec=new TestCodec();EncodedImages.Current=nextCodec;
 Check(GetImage(png) is {Width:2}&&nextCodec.Reads==1,"A decoder registered later retries a prior cached miss");
 var text=new BEngine.Editor.TextAsset("fallback");RuntimeAssetReferences.BindReference(text,png,png);
 Check(GetTextPreview(text)==File.ReadAllText(png),"Text source reads the actual file");
 var missing=Path.Combine(output,"missing.txt");var fallback=new BEngine.Editor.TextAsset("fallback");RuntimeAssetReferences.BindReference(fallback,missing,missing);
 Check(GetTextPreview(fallback)=="fallback","Missing text retains imported contents");
 Check(ResolveReference(png,"",typeof(Texture))==png,"Typed preview references preserve rooted source resolution");
 Check(DisplayName("  title  ","x/y",png)=="title"&&DisplayName(null," / A / / B / ",null)=="B"&&DisplayName(null,null,null)=="Unnamed","Shared display names preserve trim and virtual-path fallback");
 BObject.DestroyImmediate(text);BObject.DestroyImmediate(fallback);
}
finally{Invalidated-=callback;EncodedImages.Current=saved;ClearTemporaryAssetPreviews();}
var calls=new List<string>();Action<AssetPreviewCacheInvalidation> a=_=>calls.Add("a"),b=_=>calls.Add("b");
Invalidated+=a;Invalidated+=a+b;Invalidated-=a+b;Invalidate("event");
Check(calls.SequenceEqual(new[]{"a"}),"Multicast removal removes the last matching invocation subsequence");Invalidated-=a;
var owner=MakeCaller();var subscribe=owner.GetMethod("Subscribe")!;subscribe.Invoke(null,[a]);
RuntimeAssemblyCleanup.UnregisterExtensions(owner.Assembly);calls.Clear();Invalidate("event");
Check(calls.Count==0,"Retiring the registering assembly releases a callback defined elsewhere");
EditorAssetAdapters.UnregisterAssembly(typeof(Program).Assembly);
Check(EditorAssetAdapters.GetPreview(child)?.GetType().Assembly==typeof(EditorAssetPreviewSources).Assembly,"Assembly cleanup releases custom candidate caches");
EditorAssetAdapters.Register(new Adapter(typeof(PlainAsset),first));_=EditorAssetAdapters.GetPreview(generic);
var ownedType=MakeDerived(typeof(TestPreview));var ownedPreview=(IEditorAssetPreview)Activator.CreateInstance(ownedType)!;
EditorAssetAdapters.Register(new Adapter(typeof(ChildAsset),ownedPreview));
Check(ReferenceEquals(EditorAssetAdapters.GetPreview(child),ownedPreview),"An extension may supply the preview implementation of a shared adapter");
RuntimeAssemblyCleanup.UnregisterExtensions(ownedType.Assembly);
Check(ReferenceEquals(EditorAssetAdapters.GetPreview(child),first),"Retiring a preview implementation releases its adapter and cached candidate");
var weak=ExerciseCollectible();
for(var i=0;i<12&&weak.IsAlive;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
Check(!weak.IsAlive,"Cached derived asset types and retired preview registrations do not pin a collectible assembly");
var metrics=Measure(generic);Check(metrics.Bytes==0,"One hundred thousand stable preview lookups allocate zero bytes");
File.WriteAllText(Path.Combine(output,"Metrics.json"),JsonSerializer.Serialize(new{metrics.Bytes,metrics.Milliseconds}));
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(x=>x.GetName().Name is "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Rendering2D.Editor"),"Shared and Graphics preview providers load without any GUI/workbench/scene editor");
Check(!typeof(EditorAssetPreviewSources).Assembly.GetReferencedAssemblies().Any(x=>x.Name is "BEngine.Graphics" or "BEngine.Graphics.Editor" or "BEngine.IMGUI.Editor"),"Shared source metadata has no Graphics or IMGUI dependency");
EditorAssetAdapters.UnregisterAssembly(typeof(Program).Assembly);
foreach(var asset in new BAsset[]{texture,material,font,generic,child})BObject.DestroyImmediate(asset);
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("ASSET_PREVIEW_HEADLESS_OK|"+checks.Count);

[MethodImpl(MethodImplOptions.NoInlining)]
static (long Bytes,double Milliseconds) Measure(BAsset asset)
{
 for(var i=0;i<2000;i++)_=EditorAssetAdapters.GetPreview(asset);
 var timer=new Stopwatch();var before=GC.GetAllocatedBytesForCurrentThread();timer.Start();
 for(var i=0;i<100000;i++)_=EditorAssetAdapters.GetPreview(asset);
 timer.Stop();return(GC.GetAllocatedBytesForCurrentThread()-before,timer.Elapsed.TotalMilliseconds);
}
[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference ExerciseCollectible()
{
 var type=MakeDerived(typeof(PlainAsset));var asset=(BAsset)Activator.CreateInstance(type)!;
 EditorAssetAdapters.Register(new Adapter(type,new TestPreview()));_=EditorAssetAdapters.GetPreview(asset);
 var weak=new WeakReference(type.Assembly);RuntimeAssemblyCleanup.Unregister([type.Assembly]);BObject.DestroyImmediate(asset);return weak;
}
static Type MakeDerived(Type parent)
{
 var assembly=System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PreviewOwned"+Guid.NewGuid().ToString("N")),System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
 var type=assembly.DefineDynamicModule("Provider").DefineType("Provider",TypeAttributes.Public,parent);
 type.DefineDefaultConstructor(MethodAttributes.Public);return type.CreateType()!;
}
static Type MakeCaller()
{
 var assembly=System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PreviewCallbackOwner"),System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
 var type=assembly.DefineDynamicModule("Owner").DefineType("Caller",TypeAttributes.Public);
 var method=type.DefineMethod("Subscribe",MethodAttributes.Public|MethodAttributes.Static,typeof(void),[typeof(Action<AssetPreviewCacheInvalidation>)]);
 method.SetImplementationFlags(MethodImplAttributes.NoInlining);var il=method.GetILGenerator();il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);il.Emit(System.Reflection.Emit.OpCodes.Call,typeof(EditorAssetPreviewSources).GetEvent("Invalidated")!.AddMethod!);il.Emit(System.Reflection.Emit.OpCodes.Ret);return type.CreateType()!;
}
public class PlainAsset:BAsset;
public sealed class ChildAsset:PlainAsset;
sealed class Adapter(Type type,IEditorAssetPreview preview):EditorAssetAdapter{public override Type AssetType=>type;public override bool ReadOnly=>false;public override IEditorAssetPreview Preview=>preview;}
public class TestPreview:IEditorAssetPreview
{
 public bool Enabled=true;public bool CanPreview(BAsset asset)=>Enabled;public string GetIcon(BAsset asset)=>"test";public string GetInfo(BAsset asset)=>"test";public EditorAssetPreviewContent GetContent(BAsset asset)=>default;
}
sealed class TestCodec:IEncodedImageCodec
{
 public int Reads;public bool Valid=true;public bool TryGetInfo(byte[] bytes,out int width,out int height){Reads++;width=2;height=3;return Valid;}
 public bool TryDecode(ReadOnlySpan<byte> bytes,out int width,out int height,out byte[] rgba){width=height=0;rgba=[];return false;}
}
