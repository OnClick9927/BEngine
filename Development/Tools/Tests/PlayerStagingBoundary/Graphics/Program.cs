using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
 .AddSingleton<IPackageCatalogSource,EmptyCatalog>().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
var project=Path.Combine(root,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),"format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Graphics Test\n");
var workspace=ProjectWorkspace.Open(project,FileUtility.Current,PathUtility.Current);
var icons=Path.Combine(output,"Editor/Icons");Directory.CreateDirectory(icons);
foreach(var file in new[]{"BEngine.png","BEngine.Badge.png"})File.Copy(Path.Combine(args[1],file),Path.Combine(icons,file),true);
// Use the same public resource search mechanism as the editor host.
var graphics=Assembly.Load("BEngine.Graphics.Editor");RuntimeHelpers.RunModuleConstructor(graphics.ManifestModule.ModuleHandle);
var builder=RuntimeServiceContributions.GetDefault<IPlayerStartupImageBuilder>();
var codec=typeof(Texture).Assembly.GetType("BEngine.Rendering.PngImageCodec",true)!;
var encode=codec.GetMethod("EncodeRgba",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Encode>();
var decode=codec.GetMethod("TryDecode",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Decode>();
Check(builder is not null&&builder.GetType().Assembly==graphics,"Graphics owns the image-builder contribution");
PlayerStartupImages Build(string source)=>builder!.Build(workspace,new PlayerBuildRequest{ProjectPath=project,SplashImage=source});
var normal=Build("");Check(!normal.LogoPng.IsEmpty&&normal.LogoPng.Span.SequenceEqual(normal.ApplicationIconPng.Span),"Default engine logo remains unbadged and is also the application icon");
var same="Assets/Default.png";File.WriteAllBytes(Path.Combine(project,same),normal.LogoPng.ToArray());
Check(Build(same).LogoPng.Span.SequenceEqual(normal.LogoPng.Span),"Explicitly selecting the engine logo does not add a second badge");
foreach(var size in new[]{(100,100),(100,8),(1,1),(512,256)}){
 var pixels=new byte[size.Item1*size.Item2*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=10;pixels[i+1]=20;pixels[i+2]=30;pixels[i+3]=220;}
 var original=encode(size.Item1,size.Item2,pixels);var path=$"Assets/Custom-{size.Item1}-{size.Item2}.png";
 File.WriteAllBytes(Path.Combine(project,path),original);var result=Build(path);
 Check(result.ApplicationIconPng.Span.SequenceEqual(original),$"Custom application icon stays undecorated: {size}");
 Check(decode(result.LogoPng.Span,out var width,out var height,out _)&&width==size.Item1&&height>=size.Item2,$"Composed logo remains a valid PNG with room for the full badge: {size}");
 if(size.Item1>1)Check(!result.LogoPng.Span.SequenceEqual(original),$"Custom splash receives build-time branding: {size}");
 File.WriteAllBytes(Path.Combine(output,$"Logo-{size.Item1}-{size.Item2}.png"),result.LogoPng.ToArray());
}
Throws<FileNotFoundException>(()=>Build("Assets/Missing.png"),"Missing custom logo is reported without silent fallback");
File.WriteAllText(Path.Combine(project,"Assets/Invalid.png"),"not a png");
Throws<InvalidDataException>(()=>Build("Assets/Invalid.png"),"Invalid PNG is rejected by its Graphics owner");
File.WriteAllBytes(Path.Combine(project,"Assets/Wrong.jpg"),normal.LogoPng.ToArray());
Throws<InvalidDataException>(()=>Build("Assets/Wrong.jpg"),"Unsupported custom image extension remains rejected");
Throws<InvalidDataException>(()=>Build("../outside.png"),"Custom image cannot escape the project");
Check(!typeof(IPlayerStartupImageBuilder).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "BEngine.Graphics" or "BEngine.AssetBundles"),"Core image contract has no reverse reference to its package implementations");
RuntimeServiceContributions.UnregisterAssembly(graphics);
Check(RuntimeServiceContributions.GetDefault<IPlayerStartupImageBuilder>() is null,"Retiring Graphics removes its default image-builder registration");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PLAYER_STAGING_GRAPHICS_OK|"+checks.Count);
delegate byte[] Encode(int width,int height,ReadOnlySpan<byte> rgba);
delegate bool Decode(ReadOnlySpan<byte> data,out int width,out int height,out byte[] rgba);
sealed class EmptyCatalog:IPackageCatalogSource{
 public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version};
}
