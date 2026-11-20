using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
var checks=new List<string>();void Check(bool result,string name){if(!result)throw new Exception(name);checks.Add(name);}
var path=Path.Combine(output,"Settings.yaml");
File.WriteAllText(path,YamlUtility.Serialize(new ProjectSettingsData{CompanyName="Stable Company",ProductName="Stable Product"}));
EditorProjectSettings.Initialize(path);var baseline=File.ReadAllBytes(path);var tagVersion=TagManager.version;var layerVersion=LayerRegistry.version;
var changes=0;void Changed()=>changes++;EditorProjectSettings.projectSettingsChanged+=Changed;
void Invalid(Action<ProjectSettingsData> edit,string name){
 EditorProjectSettings.Initialize(path);var company=Application.companyName;var product=Application.productName;var tags=TagManager.version;var layers=LayerRegistry.version;
 EditorProjectSettings.current.CompanyName="Uncommitted Company";EditorProjectSettings.current.ProductName="Uncommitted Product";EditorProjectSettings.current.Tags.Add("Uncommitted");edit(EditorProjectSettings.current);
 try{EditorProjectSettings.Save();throw new Exception("Expected invalid settings rejection");}catch(InvalidDataException){}
 Check(File.ReadAllBytes(path).SequenceEqual(baseline),name+": rejected settings never overwrite the source file");
 Check(Application.companyName==company && Application.productName==product && TagManager.version==tags && LayerRegistry.version==layers && changes==0,name+": rejected settings never apply partial runtime state or emit a change");
}
var background=Application.runInBackground;
try{
 Invalid(x=>x.SortingLayers[0].Value=63,"Invalid layer index");
 if(args.Length>1 && args[1]=="invalid")goto Complete;
 Invalid(x=>x.SortingLayers.RemoveAt(0),"Missing built-in layer");
 Invalid(x=>x.SortingLayers[0].BuiltInId="Other","Invalid built-in identity");
 Invalid(x=>x.SortingLayers[1].BuiltInId=x.SortingLayers[0].BuiltInId,"Duplicate built-in identity");
 Invalid(x=>x.Tags.AddRange([" duplicate ","duplicate"]),"Normalized tag conflict");
 EditorProjectSettings.Initialize(path);EditorProjectSettings.current.CompanyName="Saved Company";EditorProjectSettings.current.ProductName="Saved Product";
 EditorProjectSettings.current.RunInBackground=!background;EditorProjectSettings.current.SortingLayers=null!;EditorProjectSettings.Save();
 Check(changes==1 && Application.companyName=="Saved Company" && Application.productName=="Saved Product","Valid settings apply identity and notify exactly once");
 Check(Application.runInBackground==background,"Authoring identity application preserves the host's background execution policy");
 var defaults=new RuntimeSettingsData();
 Check(EditorProjectSettings.current.SortingLayers.Select(x=>(x.Value,x.BuiltInId)).SequenceEqual(defaults.SortingLayers.Select(x=>(x.Value,x.BuiltInId))),"Missing layer lists recover defaults from the engine model");
 EditorProjectSettings.current.SortingLayers[0].Name="Independent";
 Check(new RuntimeSettingsData().SortingLayers[0].Name==defaults.SortingLayers[0].Name,"Recovered defaults do not share mutable layer instances");
 var reloaded=YamlUtility.Load<ProjectSettingsData>(path);reloaded.Validate();
 Check(reloaded.CompanyName=="Saved Company" && reloaded.RunInBackground==!background,"Saved authoring data remains valid for the engine runtime reader");
 var bridge=typeof(EditorApplication).Assembly.GetType("BEngine.Editor.EditorBridge",true)!;
 var host=DispatchProxy.Create(typeof(EditorApplication).Assembly.GetType("BEngine.Editor.IEditorHost",true)!,typeof(HostProbe));
 var state=(HostProbe)host;bridge.GetMethod("Attach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);
 var order=new List<string>();void AppPause(bool paused)=>order.Add("application:"+paused);void EditorPause(PauseState paused)=>order.Add("editor:"+paused);
 Application.pauseStateChanged+=AppPause;EditorApplication.pauseStateChanged+=EditorPause;
 try{
  EditorApplication.isPaused=true;
  Check(state.Paused && order.SequenceEqual(new[]{"application:True","editor:Paused"}),"Editor host pause state is set before forwarding application and editor notifications");
  EditorApplication.isPaused=true;Check(order.Count==2,"Setting an unchanged editor pause state does not re-notify");
  EditorApplication.isPaused=false;Check(!state.Paused && order.Skip(2).SequenceEqual(new[]{"application:False","editor:Unpaused"}),"Editor resume retains application-before-editor event ordering");
 }finally{Application.pauseStateChanged-=AppPause;EditorApplication.pauseStateChanged-=EditorPause;bridge.GetMethod("Detach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);}
 Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.EditorWorkbench.Editor" or "BEngine.IMGUI.Editor"),"Settings and pause forwarding do not instantiate the default workbench");
 Complete:;
}finally{EditorProjectSettings.projectSettingsChanged-=Changed;Application.runInBackground=background;}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("APPLICATION_HOST_WORKFLOW_OK|"+checks.Count);
public class HostProbe:DispatchProxy
{public bool Paused;protected override object? Invoke(MethodInfo? m,object?[]? args){switch(m!.Name){case "get_IsPaused":return Paused;case "set_IsPaused":Paused=(bool)args![0]!;return null;default:return m.ReturnType.IsValueType?Activator.CreateInstance(m.ReturnType):null;}}}
