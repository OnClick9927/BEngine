using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.Build;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.ProjectSystem;

DotNetPlatformServices.Initialize();
var checks=new List<string>(); var payloads=new SortedDictionary<string,string>();
void Check(bool value,string description){if(!value)throw new Exception(description);checks.Add(description);}
void Reject<T>(Action action,string description) where T:Exception
{try{action();}catch(T){Check(true,description);return;}throw new Exception(description);}
string Hash(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));
var owner=args[2];
foreach(var type in new[]{typeof(BuildTargetManifest),typeof(BuildTargetManifestSerializer),typeof(LegacyProjectWorkspace),typeof(HostPresentationConfiguration),typeof(AotInterpreterRuntimeHost),typeof(AotInterpreterManagedCodeRuntimeProvider),typeof(CoreClrManagedCodeRuntimeProvider),typeof(PlayerManagedCodeRuntimeFactory)})
    Check(type.Assembly.GetName().Name==owner,"Implementation owner: "+type.FullName);
Directory.CreateDirectory(args[1]);
foreach(var descriptor in BuildTargetUtility.Current.All)
{
    var manifest=BuildTargetManifest.FromDescriptor(descriptor);
    var yaml=BuildTargetManifestSerializer.Serialize(manifest);
    var binary=BinaryDataSerializer.CompileYaml(yaml);
    payloads[descriptor.TargetId+":yaml"]=Hash(yaml);payloads[descriptor.TargetId+":binary"]=Hash(binary);
    Check(BuildTargetManifestSerializer.Deserialize(yaml).ToDescriptor().TargetId==descriptor.TargetId,"YAML target "+descriptor.TargetId);
    Check(BuildTargetManifestSerializer.Deserialize(binary).ToDescriptor().GraphicsBackends.SequenceEqual(descriptor.GraphicsBackends),"Binary target "+descriptor.TargetId);
    BuildTargetManifestSerializer.SetCurrent(binary);
    var loaded=BuildTargetManifestSerializer.LoadCurrent(); loaded.GraphicsBackends.Clear();loaded.TargetId="mutated";
    Check(BuildTargetManifestSerializer.LoadCurrent().TargetId==descriptor.TargetId && BuildTargetManifestSerializer.LoadCurrent().GraphicsBackends.Count>0,
        "Cached manifests return independent values "+descriptor.TargetId);
    var directory=Path.Combine(args[1],descriptor.TargetId);
    BuildTargetManifestSerializer.Save(manifest,directory);
    BuildTargetManifestSerializer.SetCurrentPath(Path.Combine(directory,BuildTargetManifest.FileName));
    Check(BuildTargetManifestSerializer.LoadCurrent().TargetId==descriptor.TargetId,"File-backed target "+descriptor.TargetId);
    manifest.Version++;Reject<InvalidDataException>(()=>BuildTargetManifestSerializer.Serialize(manifest),"Reject unsupported version "+descriptor.TargetId);
}
var selected=BuildTargetUtility.Current.InferCurrentDesktop();
BuildTargetManifestSerializer.SetCurrent(BuildTargetManifestSerializer.Serialize(BuildTargetManifest.FromDescriptor(selected)));
var root=Path.Combine(args[1],"Legacy");Directory.CreateDirectory(root);
File.WriteAllText(Path.Combine(root,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: Legacy\nstartupScene: Assets/Main.scene.yaml\nwindow:\n  title: Unpackaged\n  width: 800\n  height: 600\n  vSync: false\n");
var workspace=LegacyProjectWorkspace.Open(root,FileUtility.Current,PathUtility.Current);
Check(workspace.RootPath==Path.GetFullPath(root),"Legacy project retains its runtime root");
Check(LegacyProjectWorkspace.SettingsDirectory(workspace)==Path.Combine(Path.GetFullPath(root),"ProjectSettings"),"Legacy settings path remains stable");
var options=HostPresentationConfiguration.Create(workspace.Project,workspace.Settings,new HostServices());
Check(options.Window.Width==800 && options.Window.Height==600,"Neutral window options preserve project configuration");
var provider=new CoreClrManagedCodeRuntimeProvider();Check(provider.IsAvailable,"Native assembly-domain backend is available");
var factory=new PlayerManagedCodeRuntimeFactory([provider]);var runtime=factory.CreateRuntime(new(ManagedCodeRuntimeKind.CoreClr));
Check(runtime.Kind==ManagedCodeRuntimeKind.CoreClr,"Runtime factory retains native strategy selection");
var stopped=runtime.DisposeAsync().AsBTask();while(!stopped.IsCompleted)BTaskScheduler.Main.Tick(TimeSpan.Zero);stopped.GetAwaiter().GetResult();
AotInterpreterRuntimeHost.DeclareInterpreterEnabled();Check(new AotInterpreterManagedCodeRuntimeProvider().IsAvailable,"Explicit interpreter opt-in is preserved");
File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,Payloads=payloads},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("DEPLOYMENT_OK|"+checks.Count);
sealed class HostServices:IServiceProvider
{public object? GetService(Type type)=>typeof(PlatformRuntime).GetMethod("GetService")!.MakeGenericMethod(type).Invoke(null,null);}
