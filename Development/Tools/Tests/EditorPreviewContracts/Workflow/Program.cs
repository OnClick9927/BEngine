using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Inspector=BEngine.Editor.Editor;
using Debug=BEngine.Debug;

if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Installed Windows editor integration fixture");
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",Path.Combine(root,"EditorData"));
DotNetPlatformServices.Initialize();WindowsEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
var imgui=typeof(AssetPreview).Assembly;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly(),imgui,typeof(EditorAssetPreviewSources).Assembly]);
Check(typeof(IAssetPreviewProvider).Assembly==imgui&&typeof(AssetPreviewImage).Assembly==typeof(EditorAssetPreviewSources).Assembly,"UI preview extensions belong to IMGUI; neutral image values belong to EditorServices");
var coreEditor=Assembly.Load("BEngine.Core.Editor");
Check(coreEditor.GetType("BEngine.Editor.IAssetPreviewProvider")==null&&coreEditor.GetType("BEngine.Editor.AssetPreviewImage")==null,"Core Editor no longer owns concrete preview contracts");
foreach(var type in new[]{typeof(IAssetPreviewProvider),typeof(AssetPreviewImage)})
 foreach(var suffix in new[]{"BEngine.Core.Editor","BEngine.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null"})
  Check(RuntimeTypeCache.FindType(type.FullName+", "+suffix)==type,"Legacy preview type identifier resolves: "+type.Name+" / "+suffix);
Check(new AssetPreviewImage("preview",2,3)==new AssetPreviewImage("preview",2,3),"Preview value equality remains intact");
Check(Type.GetType("BEngine.Editor.AssetPreviewImage, BEngine.IMGUI.Editor",throwOnError:true)==typeof(AssetPreviewImage),"Previously compiled IMGUI preview values resolve through the package forwarder");

var target=new PreviewTarget();
var editor=(LifetimeEditor)Inspector.CreateEditor(target,typeof(LifetimeEditor));
var enabledObject=editor.GetInstanceID();var serialized=editor.serializedObject;var lifecycle=new List<string>();
editor.OnDisabled=()=>{lifecycle.Add("disable");Check(!editor.isDestroyed,"Explicit Dispose invokes OnDisable before object destruction");editor.Dispose();};
editor.destroyed+=_=>{lifecycle.Add("destroy");editor.Dispose();};
editor.Dispose();editor.Dispose();BObject.DestroyImmediate(editor);
Check(editor.Disables==1&&lifecycle.SequenceEqual(new[]{"disable","destroy"}),"Dispose, destroyed callback reentry and later destruction are once-only and ordered");
Check(editor.isDestroyed&&BObject.FindObjectFromInstanceID(enabledObject)==null,"Disposed inspectors leave the Core object registry");
var destroyedFirst=(LifetimeEditor)Inspector.CreateEditor(target,typeof(LifetimeEditor));var events=0;destroyedFirst.destroyed+=_=>events++;
BObject.DestroyImmediate(destroyedFirst);destroyedFirst.Dispose();
Check(destroyedFirst.Disables==1&&events==1&&destroyedFirst.isDestroyed,"Destroy followed by Dispose disables and releases once");
var neverEnabled=new LifetimeEditor();neverEnabled.Dispose();
Check(neverEnabled.isDestroyed&&neverEnabled.Disables==0,"Uninitialized inspectors release their registration without a fabricated OnDisable");
var first=(LifetimeEditor)Inspector.CreateEditor(target,typeof(LifetimeEditor));Inspector? cached=first;
Inspector.CreateCachedEditor(target,typeof(LifetimeEditor),ref cached);Check(ReferenceEquals(cached,first),"Cached inspector reuse remains stable");
var other=new PreviewTarget();Inspector.CreateCachedEditor(other,typeof(LifetimeEditor),ref cached);
Check(first.isDestroyed&&cached?.target==other,"Changing cached inspector destroys the previous instance");cached!.Dispose();

using(var data=new SerializedObject(target))
{
 var special=data.FindProperty(nameof(PreviewTarget.Special))!;
 Check(EditorGUI.GetPropertyHeight(special)==501,"Attribute drawers still resolve through the public Core factory");
 Check(EditorGUI.GetPropertyHeight(special)==501&&SpecialDrawer.Constructions==1,"Repeated property inspection reuses the same drawer instance");
 Check(EditorGUI.GetPropertyHeight(data.FindProperty(nameof(PreviewTarget.Second))!)==501&&SpecialDrawer.Constructions==2,"Different properties receive different drawer instances");
 Check(EditorGUI.GetPropertyHeight(data.FindProperty(nameof(PreviewTarget.Child))!)==601,"Assignable drawer registration still supports child types");
 Check(EditorGUI.GetPropertyHeight(data.FindProperty(nameof(PreviewTarget.Exact))!)==701,"Exact drawer registration takes precedence over inherited registration");
 Check(EditorGUI.GetPropertyHeight(data.FindProperty(nameof(PreviewTarget.Tie))!)==801,"Equal matches keep deterministic drawer-name ordering");
 Check(EditorGUI.GetPropertyHeight(data.FindProperty(nameof(PreviewTarget.Private))!)!=999&&PrivateDrawer.Constructions==0,"Non-public constructors remain excluded during drawer discovery");
 var old=Debug.logger.logHandler;var capture=new CapturedLog();Debug.logger.logHandler=capture;
 try{Check(EditorGUI.GetPropertyHeight(data.FindProperty(nameof(PreviewTarget.Failing))!)>0&&capture.Errors>0,"Throwing constructors remain isolated and use the default field");}
 finally{Debug.logger.logHandler=old;}
 EditorExtensionLifecycle.Invalidate();
 Check(EditorGUI.GetPropertyHeight(special)==501&&SpecialDrawer.Constructions==3,"Extension invalidation retires cached drawers");
}

var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAnkAAAAASUVORK5CYII=");
var resources=Path.Combine(root,"SearchRoot");Directory.CreateDirectory(Path.Combine(resources,"Editor"));Directory.CreateDirectory(Path.Combine(resources,"Resources"));
File.WriteAllBytes(Path.Combine(resources,"Editor/icon.png"),png);
File.WriteAllBytes(Path.Combine(resources,"Resources/fallback.png"),png);
File.WriteAllText(Path.Combine(resources,"Resources/icon.png"),"invalid fallback should not win");
Resources.RegisterResourceRoot(resources);
try
{
 var resolver=FileGpuCanvasResourceResolver.Shared;
 Check(resolver.TryResolveTexture("icon.png?bengine-preview=1",out var icon)&&icon.Width==1&&icon.Height==1,"Editor roots and revision suffix retain priority over runtime resources");
 Check(resolver.TryResolveTexture("fallback",out _),"Runtime resource fallback and omitted file extension still work");
 Check(resolver.TryResolveTexture(Path.Combine(resources,"Editor/icon.png"),out _),"Absolute image sources still resolve");
 Check(!resolver.TryResolveTexture("missing.png",out _)&&!resolver.TryResolveTexture("../escape.png",out _),"Missing or escaping image paths remain unavailable");
 Check(!resolver.TryResolveTexture(" ",out _),"Blank image paths remain unavailable");
}
finally{Resources.UnregisterResourceRoot(resources);}

var skin=new GUISkin();var unresolved=new Texture(1,1);RuntimeAssetReferences.BindReference(unresolved,"Assets/Missing/image.png");
skin.button.normal.backgroundImage=unresolved;
var absolute=new Texture(1,1);var missingAbsolute=Path.Combine(root,"Missing/absolute.png");RuntimeAssetReferences.BindReference(absolute,missingAbsolute,missingAbsolute);skin.box.normal.backgroundImage=absolute;
var builtin=new Texture(1,1);RuntimeAssetReferences.BindReference(builtin,"builtin://gui-style/segmented-button");skin.button.hover.backgroundImage=builtin;
var skinPath=Path.Combine(root,"roundtrip.guiskin.yaml");skin.Save(skinPath);var loaded=GUISkin.Load(skinPath);
Check(loaded.button.normal.backgroundImage?.assetPath=="Assets/Missing/image.png","GUISkin preserves unresolved project texture addresses");
Check(loaded.button.normal.backgroundImage?.guid==string.Empty&&loaded.button.normal.backgroundImage.assetType==nameof(Texture),"Unresolved GUI backgrounds retain type without an invented GUID");
Check(loaded.box.normal.backgroundImage?.sourcePath==Path.GetFullPath(missingAbsolute),"GUISkin preserves canonical absolute unresolved source paths");
var segmented=loaded.button.hover.backgroundImage;
Check(segmented!.guid==string.Empty&&segmented.assetType==nameof(Texture)&&segmented.assetPath=="builtin://gui-style/segmented-button","Built-in background identity remains a virtual reference");
var roundtrip=Path.Combine(root,"again.guiskin.yaml");loaded.Save(roundtrip);
Check(File.ReadAllText(roundtrip).Contains("Assets/Missing/image.png"),"Saving a loaded GUISkin does not discard unresolved references");
var secondLoad=GUISkin.Load(roundtrip);
Check(segmented!=null&&ReferenceEquals(secondLoad.button.hover.backgroundImage,segmented),"Built-in segmented backgrounds retain their shared sentinel");
BObject.DestroyImmediate(secondLoad);BObject.DestroyImmediate(builtin);

var weak=ExerciseProvider(args[1],Check);
for(var i=0;i<12&&weak.IsAlive;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
Check(!weak.IsAlive,"Generic owner cleanup releases cached preview providers so their assembly can unload");
Check(AssetPreview.GetMiniThumbnail(new PreviewTarget())!="collectible-preview-icon","Retired providers are not reused after generation change");
BObject.DestroyImmediate(target);BObject.DestroyImmediate(other);BObject.DestroyImmediate(skin);BObject.DestroyImmediate(loaded);BObject.DestroyImmediate(unresolved);BObject.DestroyImmediate(absolute);
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PREVIEW_WORKFLOW_CONTRACTS_OK|"+checks.Count);

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference ExerciseProvider(string path,Action<bool,string> check)
{
 var context=new ProviderContext();var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(path));RuntimeTypeCache.RegisterAssemblies([assembly]);
 var asset=new PreviewTarget{name="collectible-preview-contract"};
 check(AssetPreview.GetMiniThumbnail(asset)=="collectible-preview-icon","A real collectible extension supplies Inspector thumbnails");
 check(AssetPreview.GetInfoString(asset)=="collectible-preview-info","A real collectible extension supplies Inspector information");
 RuntimeAssemblyCleanup.UnregisterExtensions(assembly);
 // No EditorExtensionLifecycle.Invalidate: package rollback uses the generic assembly cleanup path.
 RuntimeTypeCache.UnregisterAssemblies([assembly]);
 _=TypeCache.GetTypesDerivedFrom<IAssetPreviewProvider>();
 context.Unload();BObject.DestroyImmediate(asset);return new WeakReference(context);
}
public sealed class ProviderContext():AssemblyLoadContext(isCollectible:true)
{protected override Assembly? Load(AssemblyName name)=>Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==name.Name);}
public sealed class PreviewTarget:ScriptableObject
{
 [Special]public int Special;[Special]public int Second;public ChildWidget Child=new();public ExactWidget Exact=new();public TieWidget Tie=new();[Private]public int Private;[Failing]public int Failing;
}
public sealed class LifetimeEditor:Inspector
{public int Disables;public Action? OnDisabled;protected override void OnDisable(){Disables++;OnDisabled?.Invoke();}}
public sealed class SpecialAttribute:PropertyAttribute;
public sealed class PrivateAttribute:PropertyAttribute;
public sealed class FailingAttribute:PropertyAttribute;
[CustomPropertyDrawer(typeof(SpecialAttribute))]
public sealed class SpecialDrawer:PropertyDrawer{public static int Constructions;public SpecialDrawer()=>Constructions++;public override Fix64 GetPropertyHeight(SerializedProperty p,GUIContent l)=>501;}
public class Widget;public sealed class ChildWidget:Widget;public sealed class ExactWidget:Widget;public sealed class TieWidget;
[CustomPropertyDrawer(typeof(Widget),true)]public sealed class BaseWidgetDrawer:PropertyDrawer{public override Fix64 GetPropertyHeight(SerializedProperty p,GUIContent l)=>601;}
[CustomPropertyDrawer(typeof(ExactWidget))]public sealed class ExactWidgetDrawer:PropertyDrawer{public override Fix64 GetPropertyHeight(SerializedProperty p,GUIContent l)=>701;}
[CustomPropertyDrawer(typeof(TieWidget))]public sealed class ATieDrawer:PropertyDrawer{public override Fix64 GetPropertyHeight(SerializedProperty p,GUIContent l)=>801;}
[CustomPropertyDrawer(typeof(TieWidget))]public sealed class ZTieDrawer:PropertyDrawer{public override Fix64 GetPropertyHeight(SerializedProperty p,GUIContent l)=>802;}
[CustomPropertyDrawer(typeof(PrivateAttribute))]public sealed class PrivateDrawer:PropertyDrawer{public static int Constructions;private PrivateDrawer()=>Constructions++;public override Fix64 GetPropertyHeight(SerializedProperty p,GUIContent l)=>999;}
[CustomPropertyDrawer(typeof(FailingAttribute))]public sealed class FailingDrawer:PropertyDrawer{public FailingDrawer()=>throw new InvalidOperationException("expected drawer constructor failure");}
public sealed class CapturedLog:ILogHandler
{public int Errors;public void LogFormat(LogType type,BObject? context,string format,params object?[] args){if(type==LogType.Error)Errors++;}public void LogException(Exception exception,BObject? context)=>Errors++;}
