using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.App.Editor.ProjectSystem;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Editor.Build.ProjectSystem;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Installed Windows editor integration fixture");
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",Path.Combine(root,"EditorData"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
var project=Path.Combine(root,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets"));Directory.CreateDirectory(Path.Combine(project,"ProjectSettings"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),YamlUtility.Serialize(new ProjectData{Name="123 Demo",EngineVersion="0.1.0"}));
File.WriteAllText(Path.Combine(project,"ProjectSettings/ProjectSettings.yaml"),YamlUtility.Serialize(new ProjectSettingsData{CompanyName="Test Company"}));
var workspace=ProjectWorkspace.Open(project);
Directory.CreateDirectory(Path.GetDirectoryName(workspace.StartupScenePath)!);
using(var scene=new Scene("Main"))File.WriteAllText(workspace.StartupScenePath,SceneAssetDocuments.Serialize(scene));
// A relocated resource root is not an ancestor of either the process or its working directory.
var resourceRoot=Path.Combine(root,"RelocatedBranding");
var icons=Path.Combine(resourceRoot,"Editor/Icons");Directory.CreateDirectory(icons);
var expectedLogo=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAnkAAAAASUVORK5CYII=");
File.WriteAllBytes(Path.Combine(icons,"BEngine.png"),expectedLogo);
Resources.RegisterResourceRoot(resourceRoot);
try
{
var template=new PlayerProjectTemplate();template.Initialize(workspace);
Check(File.ReadAllBytes(workspace.ResolveInside(AotTemplateLayout.LogoAssetPath)).SequenceEqual(expectedLogo),"The framework copies the exact logo supplied by the registered editor resource root");
var copiedLogo=workspace.ResolveInside(AotTemplateLayout.LogoAssetPath);
var originalMeta=File.ReadAllBytes(copiedLogo+".meta");
File.WriteAllBytes(copiedLogo,[1,2,3,4]);
template.Initialize(workspace);
Check(File.ReadAllBytes(copiedLogo).SequenceEqual(new byte[]{1,2,3,4})&&File.ReadAllBytes(copiedLogo+".meta").SequenceEqual(originalMeta),"Existing custom template logos and GUID metadata are retained");
var settings=PlayerBuildSettingsStore.Load(project);
Check(settings.AndroidApplicationIdentifier=="com.testcompany.p123demo"&&settings.IosBundleIdentifier==settings.AndroidApplicationIdentifier,"Application templates delegate both project-derived identities to the build settings owner");
Check(settings.SplashImage==AotTemplateLayout.LogoAssetPath&&settings.HotResourceVersion=="v1"&&settings.Scenes.Count==1&&settings.Scenes[0].Path==AotProjectLayout.SceneAssetPath,"Template workflow settings retain their existing defaults");
var aot=workspace.ResolveInside(AotProjectLayout.AssemblyDefinitionAssetPath);var text=File.ReadAllText(aot);
var doc=YamlUtility.Deserialize<BEngine.Editor.Documents.AssemblyDefinitionDocument>(text);doc.Validate();
Check(doc.Name=="AOT"&&doc.References.Contains("BEngine.Application")&&doc.References.Contains("BEngine.UIElements"),"Generated AOT assembly uses the engine document validator");
PlayerProjectTemplate.EnsureRequiredAotInvariants(workspace);
Check(File.ReadAllText(aot)==text,"Repeated AOT invariant checks preserve an already valid document");
doc.Name="LegacyAot";doc.References.Add("LegacyAot");File.WriteAllText(aot,YamlUtility.Serialize(doc));
PlayerProjectTemplate.EnsureRequiredAotInvariants(workspace);doc=YamlUtility.Load<BEngine.Editor.Documents.AssemblyDefinitionDocument>(aot);
Check(doc.Name=="AOT"&&!doc.References.Contains("LegacyAot"),"Reserved assembly repair still removes the previous self reference");
var preserved=File.ReadAllBytes(aot);doc.Version=2;File.WriteAllText(aot,YamlUtility.Serialize(doc));var invalid=File.ReadAllBytes(aot);
try{PlayerProjectTemplate.EnsureRequiredAotInvariants(workspace);throw new Exception("Expected unsupported AOT version");}catch(InvalidDataException){}
Check(File.ReadAllBytes(aot).SequenceEqual(invalid),"Unsupported AOT documents fail before overwrite");File.WriteAllBytes(aot,preserved);
settings.AndroidApplicationIdentifier="com.custom.android";settings.IosBundleIdentifier="com.custom.ios";PlayerBuildSettingsStore.Save(project,settings);
var custom=PlayerBuildSettingsStore.Load(project);Check(custom.AndroidApplicationIdentifier==settings.AndroidApplicationIdentifier&&custom.IosBundleIdentifier==settings.IosBundleIdentifier,"Explicit developer publication identifiers are preserved");
// Public copy skips existing projects before any package discovery or template file access.
var before=Directory.GetFiles(project,"*",SearchOption.AllDirectories).Order().ToArray();PlayerGameProjectTemplate.Copy(workspace);
Check(Directory.GetFiles(project,"*",SearchOption.AllDirectories).Order().SequenceEqual(before),"Existing projects are never inferred to be new template destinations");
Check(typeof(SceneBuildDataCompiler).Assembly.GetName().Name=="BEngine.Core.Editor","Scene-format build capability belongs to Core Editor");
Check(typeof(SceneBuildDataCompiler).Assembly==typeof(EditorWorkBatch).Assembly,"Scene-format compiler and shared preparation live in Core Editor");
Check(!typeof(PlayerProjectTemplate).Assembly.GetCustomAttributes<System.Runtime.Versioning.SupportedOSPlatformAttribute>().Any(a=>a.PlatformName.StartsWith("windows",StringComparison.OrdinalIgnoreCase)),"Application templates no longer carry the legacy Windows-only assembly constraint");
var graphics=Assembly.Load("BEngine.Graphics.Editor");
var compiler=(BEngine.ProjectSystem.Editor.IProjectShaderCompiler)Activator.CreateInstance(graphics.GetType("BEngine.ProjectSystem.Editor.CgProjectShaderCompiler",true)!,true)!;
var deleted=compiler.CompileChangedInBackground(workspace,["Assets/Missing.frag.cg"]);
Check(deleted.DeletedArtifacts.Count==1&&deleted.DeletedAssets.Single()=="Assets/Missing.frag.cg","Graphics returns retirement paths for a removed source through the public result contract");
var pointer=deleted.DeletedArtifacts.Values.Single();Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);File.WriteAllText(pointer,"old");compiler.ApplyCompilationResult(deleted);
Check(!File.Exists(pointer),"Graphics retains ownership of deleting its retired current-artifact pointer");
compiler.ApplyCompilationResult(deleted);Check(!File.Exists(pointer),"Repeated retirement remains harmless when the artifact is already absent");
}
finally { Resources.UnregisterResourceRoot(resourceRoot); }
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("EDITOR_BUILD_INTEGRATION_OK|"+checks.Count);
