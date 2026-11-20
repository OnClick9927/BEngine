using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Project");Directory.CreateDirectory(Path.Combine(root,"Assets"));
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception
{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
DotNetPlatformServices.Initialize();
var files=DispatchProxy.Create<IFileSystem,FaultFiles>();var fault=(FaultFiles)(object)files;
fault.Inner=PhysicalFileSystem.Shared;
using var services=new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton<IFileSystem>(files)
    .AddSingleton(new FileUtility(files)).BuildServiceProvider();
using var scope=EditorServiceScope.Attach(services);
string Source(string path){var full=Path.GetFullPath(Path.Combine(root,path));Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllText(full,"owner");return full;}
var owner=Source("Assets/main.payload");
var id=Guid.NewGuid();YamlUtility.Save(new AssetMetaDocument{Guid=id.ToString("N")},owner+".meta");
var sourceBytes=File.ReadAllBytes(owner);var ownerMeta=File.ReadAllBytes(owner+".meta");
var stored=GeneratedAssetArtifacts.Write(owner,71,"BYTES",[1,2,3]);
Check(stored.OwnerGuid==id&&stored.LocalIdentifier==71&&stored.Reference==$"guid:{id:N}#subasset=71","Public importer API preserves owner/local addresses");
Check(stored.ArtifactPath==Path.Combine(root,"Library","Artifacts",id.ToString("N")[..2],$"{id:N}.71.bytes"),"Store chooses the owner's Library path and normalizes the extension");
var child=YamlUtility.Load<AssetMetaDocument>(stored.ArtifactPath+".meta");
Check(child.ParentGuid==id.ToString("N")&&child.LocalIdentifier==71&&Guid.TryParse(child.Guid,out _),"Generated metadata records an independent child GUID and its parent identity");
Check(File.ReadAllBytes(owner).SequenceEqual(sourceBytes)&&File.ReadAllBytes(owner+".meta").SequenceEqual(ownerMeta),"Output publication preserves existing owner source and metadata bytes");
var rewritten=GeneratedAssetArtifacts.Write(owner,71,".bytes",[4,5]);
Check(stored==rewritten&&YamlUtility.Load<AssetMetaDocument>(stored.ArtifactPath+".meta").Guid==child.Guid,"Reimport retains reference, physical path and child GUID");
Check(File.ReadAllBytes(stored.ArtifactPath).SequenceEqual(new byte[]{4,5}),"Reimport publishes the new content");
var original=File.ReadAllBytes(stored.ArtifactPath);var originalMeta=File.ReadAllBytes(stored.ArtifactPath+".meta");
fault.FailOnce=(name,arguments)=>name=="MoveFile"&&Equals(arguments[1],stored.ArtifactPath+".meta");
Throws<IOException>(()=>GeneratedAssetArtifacts.Write(owner,71,".bytes",[9]),"Metadata publication failure reaches the consumer");
Check(File.ReadAllBytes(stored.ArtifactPath).SequenceEqual(original)&&File.ReadAllBytes(stored.ArtifactPath+".meta").SequenceEqual(originalMeta),"A failed overwrite restores exact old artifact and metadata bytes");
fault.FailOnce=(name,arguments)=>name=="MoveFile"&&Equals(arguments[1],stored.ArtifactPath);
Throws<IOException>(()=>GeneratedAssetArtifacts.Write(owner,71,".bytes",[8]),"Artifact publication failure reaches the consumer");
Check(File.ReadAllBytes(stored.ArtifactPath).SequenceEqual(original)&&File.ReadAllBytes(stored.ArtifactPath+".meta").SequenceEqual(originalMeta),"A failed artifact publish retains the previous pair");
var second=GeneratedAssetArtifacts.Write(owner,72,".bytes",[6]);
fault.FailOnce=(name,arguments)=>name=="MoveFile"&&Equals(arguments[0],stored.ArtifactPath);
Throws<IOException>(()=>GeneratedAssetArtifacts.Delete(owner,71),"Deletion stage failure reaches the consumer");
Check(File.ReadAllBytes(stored.ArtifactPath).SequenceEqual(original)&&File.ReadAllBytes(stored.ArtifactPath+".meta").SequenceEqual(originalMeta),"Failed deletion restores the active artifact and metadata pair");
GeneratedAssetArtifacts.Delete(owner,71);
Check(!File.Exists(stored.ArtifactPath)&&!File.Exists(stored.ArtifactPath+".meta")&&File.Exists(second.ArtifactPath),"Delete removes exactly one local identity");
GeneratedAssetArtifacts.Delete(owner,71);Check(true,"Deleting a missing generated identity is idempotent");
Check(File.ReadAllBytes(owner).SequenceEqual(sourceBytes)&&File.ReadAllBytes(owner+".meta").SequenceEqual(ownerMeta),"Delete preserves the owner source and metadata");
var collisionMeta=YamlUtility.Load<AssetMetaDocument>(owner+".meta");
collisionMeta.SubAssets.Add(new SubAssetMetaDocument{LocalIdentifier=73});YamlUtility.Save(collisionMeta,owner+".meta");
Throws<InvalidOperationException>(()=>GeneratedAssetArtifacts.Write(owner,73,".bytes",[7]),"Embedded-object identity collisions cannot be overwritten");
Check(!File.Exists(Path.Combine(Path.GetDirectoryName(stored.ArtifactPath)!,$"{id:N}.73.bytes")),"Rejected identity collision leaves no new artifact");
var fresh=Source("Assets/invalid.payload");
Throws<ArgumentException>(()=>GeneratedAssetArtifacts.Write(fresh,1,"../escape",[1]),"Invalid extension is rejected before filesystem mutation");
Throws<ArgumentOutOfRangeException>(()=>GeneratedAssetArtifacts.Write(fresh,0,".bytes",[1]),"Nonpositive local identifiers are rejected");
Throws<ArgumentNullException>(()=>GeneratedAssetArtifacts.Write(fresh,1,".bytes",null!),"Missing contents are rejected");
Check(!File.Exists(fresh+".meta"),"Rejected arguments do not create owner metadata");
var outside=Source("Outside/file.payload");
Throws<InvalidDataException>(()=>GeneratedAssetArtifacts.Write(outside,1,".bytes",[1]),"An owner outside Assets or Packages is rejected");
Check(!File.Exists(outside+".meta"),"Rejected outside owner creates no metadata");
Throws<FileNotFoundException>(()=>GeneratedAssetArtifacts.Write(Path.Combine(root,"Assets","missing.payload"),1,".bytes",[1]),"Missing owner source cannot acquire generated data");
var packageOwner=Source("Packages/sample/source.payload");var packageArtifact=GeneratedAssetArtifacts.Write(packageOwner,1,".bytes",[2]);
Check(packageArtifact.ArtifactPath.StartsWith(Path.Combine(root,"Library","Artifacts")+Path.DirectorySeparatorChar),"Package import output shares the owning project's Library");
var beforeMissing=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length;
GeneratedAssetArtifacts.Delete(Path.Combine(root,"Assets","absent.payload"),1);
Check(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length==beforeMissing,"Deleting an absent owner creates no files");
Throws<InvalidOperationException>(()=>AssetDatabase.GetAssetSourcePath("Assets/main.payload"),"Active-project source resolution does not invent a project in a headless importer");
Check(typeof(GeneratedAssetArtifacts).Assembly==typeof(AssetDatabase).Assembly&&typeof(GeneratedAssetArtifact).Assembly==typeof(AssetDatabase).Assembly,"Generated storage contract and implementation share the neutral editor owner");
Check(!typeof(GeneratedAssetArtifacts).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor" or "BEngine.TextureAtlas"),"Neutral service has no renderer, GUI or importer-domain dependency");
Check(!typeof(GeneratedAssetArtifacts).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==typeof(FaultFiles).Assembly.GetName().Name),"Independent import consumer needs no friendship");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.TextureAtlas"),"Store writes, rewrites and deletes without loading a window or graphics package");
Check(!Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Any(p=>p.EndsWith(".importing")||p.EndsWith(".rollback")||p.EndsWith(".deleting")||p.EndsWith(".tmp")),"Successful operations and recoverable failures leave no transaction files");
// Attach only a test host to exercise the public active-project and Play Mode guards.
// Production importer calls above use no host, reflection or internal methods.
var bridge=typeof(AssetDatabase).Assembly.GetType("BEngine.Editor.EditorBridge",true)!;
var proxy=DispatchProxy.Create(bridge.Assembly.GetType("BEngine.Editor.IEditorHost",true)!,typeof(GuardHost));
var host=(GuardHost)proxy;host.Root=root;
bridge.GetMethod("Attach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[proxy]);
try
{
    var resolved=new[]{AssetDatabase.GetAssetSourcePath("Assets/main.payload"),AssetDatabase.GetAssetSourcePath("main.payload"),AssetDatabase.GetAssetSourcePath(owner)};
    File.WriteAllText(Path.Combine(output,"SourcePaths.json"),JsonSerializer.Serialize(new{Expected=owner,Resolved=resolved}));
    Check(resolved.All(path=>path==owner),"Public source resolution accepts project, Assets-relative and absolute source paths");
    Throws<InvalidDataException>(()=>AssetDatabase.GetAssetSourcePath("../Outside/file.payload"),"Public source resolution rejects escaping the Assets root");
    Throws<InvalidDataException>(()=>GeneratedAssetArtifacts.Write(outside,1,".bytes",[1]),"An active host does not admit generated owners outside Assets or Packages");
    host.Playing=true;
    var playArtifact=GeneratedAssetArtifacts.Write(owner,72,".bytes",[9]);
    Check(playArtifact==second&&File.ReadAllBytes(second.ArtifactPath).SequenceEqual(new byte[]{9}),"Play Mode background import can publish generated Library data");
    GeneratedAssetArtifacts.Delete(owner,72);
    Check(!File.Exists(second.ArtifactPath)&&!File.Exists(second.ArtifactPath+".meta"),"Play Mode background reimport can retire generated Library data");
    Check(File.ReadAllBytes(owner).SequenceEqual(sourceBytes),"Play Mode generated import leaves owner source content intact");
}
finally{bridge.GetMethod("Detach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[proxy]);}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("GENERATED_ARTIFACT_CONTRACTS_OK|"+checks.Count);

public class FaultFiles:DispatchProxy
{
    public IFileSystem Inner=null!;
    public Func<string,object?[],bool>? FailOnce;
    protected override object? Invoke(MethodInfo? targetMethod,object?[]? arguments)
    {
        var method=targetMethod!;var args=arguments??[];
        if(FailOnce?.Invoke(method.Name,args)==true){FailOnce=null;throw new IOException("Injected publication failure");}
        try{return method.Invoke(Inner,args);}
        catch(TargetInvocationException error)when(error.InnerException is not null)
        {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
}

public class GuardHost:DispatchProxy
{
    public string Root="";public bool Playing;
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch
    {
        "get_ProjectRootPath"=>Root,"get_AssetsRootPath"=>Path.Combine(Root,"Assets"),
        "get_IsPlaying"=>Playing,"get_IsChangingPlayMode"=>false,
        "FindAssets"=>Array.Empty<EditorAssetInfo>(),
        _=>method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null
    };
}
