using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using EditorText=BEngine.Editor.TextAsset;
using Font=BEngine.Font;
using Color=BEngine.Color;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var common=Path.GetFullPath(args[1]);Directory.CreateDirectory(common);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",Path.Combine(output,"EditorData"));
DotNetPlatformServices.Initialize();WindowsEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
RuntimeHelpers.RunModuleConstructor(typeof(TextureImporter).Module.ModuleHandle);
RuntimeTypeCache.RegisterAssemblies([typeof(AssetPreview).Assembly,typeof(TextureImporter).Assembly,typeof(SpritePreviewProvider).Assembly,typeof(TextureAtlasPreviewProvider).Assembly,Assembly.GetExecutingAssembly()]);
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
var png=Path.Combine(common,"sample.png");File.WriteAllBytes(png,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAnkAAAAASUVORK5CYII="));File.SetLastWriteTimeUtc(png,new DateTime(2026,11,1,1,0,0,DateTimeKind.Utc));
var code=Path.Combine(common,"source.shader");File.WriteAllText(code,"shader\r\n\tline\u0001\nlast");File.SetLastWriteTimeUtc(code,new DateTime(2026,11,1,1,0,0,DateTimeKind.Utc));
DefaultAsset Source(string label,string path,string type){return DefaultAsset.CreateReadOnlyReference(label,path,path,type);}
T Bind<T>(T asset,string path,string type)where T:BAsset{RuntimeAssetReferences.BindReference(asset,path,path);RuntimeAssetReferences.EnsureAssetType(asset,type);return asset;}
var missing=Path.Combine(common,"absent.png");var font=(Font)Activator.CreateInstance(typeof(Font),true)!;font.name="Font fixture";
var shader=Shader.Find("Fixture/Shader");var material=new Material(shader){name="Material fixture",color=new Color(Fix64.Half,0,1,1)};
var dataType=typeof(PrefabAsset).Assembly.GetType("BEngine.Serialization.PrefabAssetData")!;
var document=Activator.CreateInstance(dataType)!;var objectType=typeof(PrefabAsset).Assembly.GetType("BEngine.Serialization.GameObjectData")!;var objectData=Activator.CreateInstance(objectType)!;var objectId=Guid.Parse("7bdef1bc-947d-47bd-a380-c2bd82fb60eb");objectType.GetProperty("Id")!.SetValue(objectData,objectId);dataType.GetProperty("Root")!.SetValue(document,objectId);((System.Collections.IList)dataType.GetProperty("GameObjects")!.GetValue(document)!).Add(objectData);
var prefab=(PrefabAsset)Activator.CreateInstance(typeof(PrefabAsset),BindingFlags.Instance|BindingFlags.NonPublic,null,[document,Path.Combine(common,"sample.prefab.yaml")],null)!;
var scene=new Scene("Scene fixture");
var assets=new Dictionary<string,BObject>{
 ["Texture"]=Bind(new Texture(1,1),png,"Texture"),["UnavailableTexture"]=Bind(new Texture(1,1),missing,"Texture"),
 ["RenderTexture"]=new RenderTexture(16,24),["Material"]=material,["Shader"]=shader,["Font"]=font,
 ["Script"]=new MonoScript{name="Script fixture"},["RuntimeText"]=new BEngine.TextAsset("one\r\ntwo\tthree", ""),
 ["EditorText"]=Bind(new EditorText("fallback"),code,"Text"),["ShaderSource"]=Bind(new EditorText("fallback"),code,"Shader"),
 ["SourceImage"]=Source("Image fixture",png,"Texture"),["SourceImageBySuffix"]=Source("Image fixture",png,"Unknown"),
 ["UnavailableImage"]=Source("Missing",missing,"Image"),["Folder"]=Source("",common,"Folder"),
 ["GenericSource"]=Source(" ",Path.Combine(common,"unknown.blob"),"Unknown"),
 ["Asmdef"]=new AssemblyDefinitionAsset(),["Prefab"]=prefab,["Scene"]=scene,["Generic"]=new PlainAsset{name="Generic fixture"},["Scriptable"]=new PlainScriptable{name="Scriptable fixture"},["NonAsset"]=new GameObject("Object fixture"),["Sprite"]=new Sprite{Texture=png},["Atlas"]=new TextureAtlas{Texture=png,Width=1,Height=1,Sprites=[new TextureAtlasSprite{Name="One",Width=1,Height=1}]}};
var begin=typeof(GUI).GetMethod("BeginFrame",BindingFlags.Static|BindingFlags.NonPublic)!;
var end=typeof(GUI).GetMethod("EndFrame",BindingFlags.Static|BindingFlags.NonPublic)!;
var commands=new List<GpuCanvasCommand>();var snapshots=new SortedDictionary<string,object>();
foreach(var (name,asset) in assets)
{
 var bySize=new List<GpuCanvasCommand[]>();
 foreach(var area in new[]{new Rect(10,20,320,240),new Rect(0,0,42,30),new Rect(0,0,0,24)})
 {
  commands.Clear();begin.Invoke(null,[new Event{type=EventType.Repaint,rawType=EventType.Repaint},800,600,commands]);
  try{AssetPreview.DrawAssetPreview(asset,area);}finally{end.Invoke(null,null);}
  bySize.Add(commands.ToArray());
 }
 Check(bySize[0].Length>0&&bySize[^1].Length==0,name+" draws a preview and skips empty areas");
 snapshots.Add(name,new{HasPreview=AssetPreview.HasPreview(asset),Image=AssetPreview.GetAssetPreview(asset),Icon=AssetPreview.GetMiniThumbnail(asset),Info=AssetPreview.GetInfoString(asset),Frames=bySize});
}
File.WriteAllText(Path.Combine(output,"Snapshot.json"),JsonSerializer.Serialize(snapshots));
#if !LEGACY_INSPECTOR
Check(typeof(AssetPreviewImage).Assembly==typeof(EditorAssetPreviewSources).Assembly,"Neutral image value moved to EditorServices");
Check(Type.GetType("BEngine.Editor.AssetPreviewImage, BEngine.IMGUI.Editor",true)==typeof(AssetPreviewImage),"Old compiled image type forwards to its new owner");
var oldContext=new OldProviderContext();var oldAssembly=oldContext.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
var oldProvider=(IAssetPreviewProvider)Activator.CreateInstance(oldAssembly.GetTypes().Single(t=>typeof(IAssetPreviewProvider).IsAssignableFrom(t)))!;
var oldTarget=new PlainScriptable{name="collectible-preview-contract"};
Check(oldProvider.CanPreview(oldTarget)&&oldProvider.GetPreview(oldTarget)==null&&oldProvider.GetIcon(oldTarget)=="collectible-preview-icon","An unchanged plugin compiled against the prior IMGUI image type executes in the new editor");
RuntimeTypeCache.RegisterAssemblies([oldAssembly]);
Check(AssetPreview.GetMiniThumbnail(oldTarget)=="collectible-preview-icon"&&AssetPreview.GetInfoString(oldTarget)=="collectible-preview-info","Legacy UI preview extensions retain priority over asset-domain adapters");
RuntimeAssemblyCleanup.UnregisterExtensions(oldAssembly);RuntimeTypeCache.UnregisterAssemblies([oldAssembly]);oldContext.Unload();BObject.DestroyImmediate(oldTarget);
#endif
foreach(var asset in assets.Values)if(asset!=shader)BObject.DestroyImmediate(asset);
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("ASSET_PREVIEW_WORKFLOW_OK|"+checks.Count);
public sealed class PlainAsset:BAsset;
public sealed class PlainScriptable:ScriptableObject;
#if !LEGACY_INSPECTOR
public sealed class OldProviderContext():System.Runtime.Loader.AssemblyLoadContext(isCollectible:true)
{protected override Assembly? Load(AssemblyName name)=>Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==name.Name);}
#endif
