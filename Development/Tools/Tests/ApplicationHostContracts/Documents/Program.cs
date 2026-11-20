using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);DotNetPlatformServices.Initialize();
var initialFiles=Directory.GetFileSystemEntries(output).Order().ToArray();
var checks=new List<string>();void Check(bool result,string name){if(!result)throw new Exception(name);checks.Add(name);}
void Invalid(Action<ProjectSettingsData> edit,string name){var value=new ProjectSettingsData();edit(value);try{value.Validate();}catch(InvalidDataException){Check(true,name);return;}throw new Exception(name);}
var tags=TagManager.version;var layers=LayerRegistry.version;var company=Application.companyName;var product=Application.productName;
var good=new ProjectSettingsData();good.ScriptingDefineSymbols.Add("HOST_VALID");good.Tags.Add("Custom");good.Validate();
Check(true,"Default settings and valid custom symbols pass the engine document validator");
Invalid(x=>x.Format="Other","Foreign authoring format is rejected");
Invalid(x=>x.Version=2,"Unsupported authoring version is rejected");
Invalid(x=>x.CompanyName="","Missing runtime identity is rejected");
Invalid(x=>x.ScriptingDefineSymbols.Add("bad-symbol"),"Invalid scripting symbols are rejected");
Invalid(x=>x.ScriptingDefineSymbols.AddRange(["DUP","DUP"]),"Duplicate scripting symbols are rejected");
Invalid(x=>x.ScriptingDefineSymbols=null!,"Null scripting symbol list is rejected");
Invalid(x=>x.Tags.AddRange([" custom ","custom"]),"Tags conflicting after runtime normalization are rejected");
Invalid(x=>x.SortingLayers[0].Value=63,"Noncontiguous layer values are rejected before configuration");
Invalid(x=>x.SortingLayers.RemoveAt(0),"Missing built-in layers are rejected");
Invalid(x=>x.SortingLayers[0].BuiltInId="Unknown","Unknown built-in identities are rejected");
Invalid(x=>x.SortingLayers[1].BuiltInId=x.SortingLayers[0].BuiltInId,"Duplicate built-in identities are rejected");
Invalid(x=>x.SortingLayers[1].Name=x.SortingLayers[0].Name.ToUpperInvariant(),"Layer display names remain case-insensitively unique");
var extension=new ValidationExtension();RuntimeServiceContributions.RegisterDefault<IRuntimeConfigurationExtension>(extension);
try{good.Validate();Check(extension.Calls==1,"Document validation invokes the active engine extension once");extension.Reject=true;try{good.Validate();throw new Exception("Expected extension rejection");}catch(InvalidDataException){Check(true,"Platform or feature extension validation remains authoritative");}}
finally{RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());}
Check(TagManager.version==tags && LayerRegistry.version==layers && Application.companyName==company && Application.productName==product,"Successful and rejected validation never mutate active runtime configuration");
Check(Directory.GetFileSystemEntries(output).Order().SequenceEqual(initialFiles),"Document validation performs no file writes");
Check(typeof(LayerRegistry).Assembly.GetName().Name=="BEngine.Core" &&
    Type.GetType("BEngine.LayerRegistry, BEngine.Core")==typeof(LayerRegistry),
    "Layer registry lives in Math and remains available through the old assembly");
var remappedLayers=LayerRegistry.CreateDefaults();
remappedLayers[0]=remappedLayers[0] with { BuiltInId="TransparentFX" };
remappedLayers[1]=remappedLayers[1] with { BuiltInId="Default" };
try{
    LayerRegistry.Configure(remappedLayers);
    var objectDataType=Assembly.Load("BEngine.Core").GetType("BEngine.Serialization.GameObjectData",throwOnError:true)!;
    var objectData=Activator.CreateInstance(objectDataType,nonPublic:true)!;
    Check(SortingLayer.Default==2 && (ulong)objectDataType.GetProperty("Layer")!.GetValue(objectData)! == 2,
        "Serialized objects use the configured default layer after built-in layers are reordered");
}finally{LayerRegistry.Configure(null);}
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Core.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.IMGUI.Editor" or "BEngine.Graphics"),"Core Editor settings validation requires no presentation package");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("APPLICATION_HOST_DOCUMENTS_OK|"+checks.Count);
public sealed class ValidationExtension:IRuntimeConfigurationExtension
{public int Calls;public bool Reject;public void Validate(RuntimeProjectData project){}public void Validate(RuntimeSettingsData settings){Calls++;if(Reject)throw new InvalidDataException("Fixture extension rejection");}}
