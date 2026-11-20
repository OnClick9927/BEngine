using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",root);
DotNetPlatformServices.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().BuildServiceProvider();PlatformRuntime.Initialize(services);
var checks=new List<string>();
void Check(bool result,string name){if(!result)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS|"+name);}
void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception(name);}
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var query=SerializationMetadata.GetObjectMembers(typeof(Derived));var members=query.ToArray();
Check(query is not MemberInfo[] && query is not IList<MemberInfo>,"Object member query does not expose the mutable reflection-cache array");
Check(members.Count(m=>m.Name=="Shared")==2 && members.First(m=>m.Name=="Shared").DeclaringType==typeof(Derived),"Inherited shadowed members retain derived-first declaration order");
Check(members.Any(m=>m.Name=="_privateBase") && members.Any(m=>m.Name=="_privateDerived"),"Inherited private SerializeField and SerializeReference fields are retained");
Check(members.Any(m=>m.Name=="Hidden"),"Inspector hiding does not remove persistent object data");
Check(members.Any(m=>m.Name=="Id") && members.Any(m=>m.Name=="gameObject"),"Ordinary nested objects do not inherit component-root name exclusions");
Check(members.All(m=>m.Name is not ("Ignored" or "NotSaved" or "ReadOnly" or "Static" or "Callback" or "ReadOnlyProperty" or "Item")),"Ignored, nonserialized, readonly, static, delegate and indexer members are excluded");
Check(members.Count(m=>m.Name=="Automatic")==1 && members.All(m=>!m.Name.Contains("k__BackingField")),"Automatic properties have no duplicate backing-field entry");
Check(SerializationMetadata.GetObjectMembers(typeof(Derived)).SequenceEqual(members),"Repeated object queries retain the same member identities and order");
SerializationMetadata.RegisterProvider(new MetadataProvider(true));
Check(!query.Any(m=>m.Name=="Automatic"),"Deferred member filtering observes the active serialization metadata provider");
SerializationMetadata.RegisterProvider(new MetadataProvider(false));
Check(query.Any(m=>m.Name=="Automatic"),"Replacing metadata policy does not leave a second stale member-plan cache");
Reject(()=>SerializationMetadata.GetObjectMembers(null!),"Object member query rejects a missing type");

var folder=new ResourceFolder("Editor");var relative="data-"+Guid.NewGuid().ToString("N")+".bin";
Directory.CreateDirectory(Path.Combine(root,"Editor"));Directory.CreateDirectory(Path.Combine(root,"Resources"));
File.WriteAllBytes(Path.Combine(root,"Editor",relative),[1,2,3]);File.WriteAllBytes(Path.Combine(root,"Resources",relative),[4,5]);
Resources.RegisterResourceRoot(root);var provider=new BytesProvider();Resources.RegisterResourceProvider(provider);
try{
 Check(folder.Name=="Editor" && folder.Load<byte[]>(relative)!.SequenceEqual(new byte[]{1,2,3}),"Named folder loads bytes through normal registered resource roots");
 Check(((byte[])folder.Load(relative,typeof(byte[]))!).SequenceEqual(new byte[]{1,2,3}),"Typed and generic folder loads share existing decoding behavior");
 Check(Path.GetFullPath(folder.FindPath(relative)!)==Path.Combine(root,"Editor",relative),"Folder path lookup uses the same root resolver");
 Check(Resources.Load<byte[]>(relative)!.SequenceEqual(new byte[]{4,5}),"A named folder does not change the standard Resources namespace");
 Check(folder.Load<byte[]>("virtual")!.SequenceEqual(new byte[]{7,8}) && provider.LastFolder=="Editor","Virtual resource providers receive the caller's folder name");
 Check(folder.Load<byte[]>("missing-"+relative)==null && folder.FindPath("missing-"+relative)==null,"Missing folder resources retain null lookup behavior");
 Reject(()=>folder.Load(relative,null!),"Dynamic resource load rejects a missing type");
}finally{Resources.UnregisterResourceProvider(provider);Resources.UnregisterResourceRoot(root);}
foreach(var invalid in new[]{""," ",".","..","a/b","a\\b","C:"})Reject(()=>new ResourceFolder(invalid),"Folder rejects invalid name "+JsonSerializer.Serialize(invalid));
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Core.Editor" or "BEngine.Graphics"),"Core metadata and folder access require no editor or graphics implementation");
Check(!typeof(BObject).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==Assembly.GetExecutingAssembly().GetName().Name),"The Core consumer has no private-access grant");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,RunDirectory=root},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("EDITOR_ACCESS_CORE_OK|"+checks.Count);

public class Base
{
 [SerializeField] private int _privateBase=1;
 public int Shared=2;
 public int PrivateValue()=>_privateBase;
}
public sealed class Derived:Base
{
 [SerializeReference] private object _privateDerived=new();
 public new int Shared=3;
 [HideInInspector] public int Hidden;
 [SerializationIgnore] public int Ignored;
 [NonSerialized] public int NotSaved;
 public readonly int ReadOnly=4;
 public static int Static;
 public Action? Callback;
 public int Id {get;set;}
 public string gameObject {get;set;}="ordinary";
 public int Automatic {get;set;}
 public int ReadOnlyProperty=>5;
 public int this[int index]{get=>index;set{}}
 public object PrivateReference()=>_privateDerived;
}
public sealed class MetadataProvider(bool hideAutomatic):ISerializationMetadataProvider
{public SerializationMemberInfo GetMember(MemberInfo member)=>SerializationMetadata.GetDefaultMember(member) with {Ignored=hideAutomatic && member.Name=="Automatic" || SerializationMetadata.GetDefaultMember(member).Ignored};}
public sealed class BytesProvider:IResourceProvider
{
 public string? LastFolder;
 public bool TryLoad(string path,string folderName,out ResourceContent content){LastFolder=folderName;content=new("virtual",[7,8]);return path=="virtual";}
 public IEnumerable<string> Enumerate(string path,string folderName)=>[];
}
