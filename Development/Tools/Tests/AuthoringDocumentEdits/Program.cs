using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

new Probe(args[0], Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).Run();

sealed class Probe(string mode, string output, string legacy)
{
    readonly List<string> checks=[];
    readonly List<object> legacyInputs=[];
    int parity;
    void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
    void Throws<T>(Action action,string name) where T:Exception
    {try{action();}catch(T){Check(true,name);return;}throw new InvalidOperationException(name);}
    static object? Invoke(Type type,string name,object? target,params object?[] args)
    {
        try{return type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance)!.Invoke(target,args);}
        catch(TargetInvocationException ex) when(ex.InnerException is {} inner){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();throw;}
    }
    public void Run()
    {
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);
        Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",Path.Combine(output,"EditorData"));
        DotNetPlatformServices.Initialize();
        Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a=>a.AssemblyName.Split(',')[0]==typeof(Probe).Assembly.GetName().Name),"Independent consumer has no Core private access");
        switch(mode){case "Core":Core();break;case "Baselines":Baselines();break;case "Workbench":Workbench();break;default:throw new ArgumentException(mode);}
        if(mode!="Workbench")Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Application" or "BEngine.EditorWorkbench.Editor" or "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor"),"Document mechanism needs no application, workbench or graphics provider");
        File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Names=checks,PreviousBinaryComparisonCases=parity,LegacyInputs=legacyInputs},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("AUTHORING_DOCUMENT_EDITS_OK|"+mode+"|"+checks.Count);
    }
    static JsonObject Member(string name,string kind,string type,string value)=>new(){["Name"]=name,["Value"]=new JsonObject{["Kind"]=kind,["Type"]=type,["Value"]=value}};
    static JsonObject Component(int seed)=>new(){["Id"]=Guid.NewGuid(),["Type"]="Absent.Component, Absent.Assembly",["Fields"]=new JsonObject{
        ["sortingLayer"]=(seed%5+1).ToString(),["cullingMask"]=(seed%63).ToString(),["enemyLayerMask"]=ulong.MaxValue.ToString(),["unrelated"]="2",["invalidLayerMask"]="bad"},
        ["Graph"]=new JsonObject{["Members"]=new JsonArray(Member("SORTINGLAYER","Scalar","System.UInt64, System.Private.CoreLib",(seed%5+1).ToString()),Member("cullingMask","Scalar","System.UInt64",(seed%63).ToString()),Member("wrongLayerMask","Scalar","System.Int32","2"),Member("otherLayerMask","Scalar"," System.UInt64 "," 2 "),Member("emptyLayerMask","Scalar","","2"),Member("nestedLayerMask","Object","System.UInt64","2"),Member("referenceLayerMask","Reference","System.UInt64","2"),Member("arrayLayerMask","Array","System.UInt64","2"))}};
    static SerializedHierarchy Sample(int seed=0)
    {
        var transform=Component(seed);
        var component=Component(seed+1);
        var nested=(JsonObject)component["Graph"]!["Members"]![5]!["Value"]!;
        nested["Members"]=new JsonArray(Member("sortingLayer","Scalar","System.UInt64","2"));
        return SerializedHierarchy.FromJson(new JsonArray(new JsonObject{["Id"]=Guid.NewGuid(),["Name"]="Unknown resource owner",["Tag"]=seed%2==0?"Old":"Other",["Layer"]=(ulong)(seed%5+1),["Transform"]=transform,["Components"]=new JsonArray(component)}));
    }
    void Core()
    {
        var data=Sample();var before=data.ToJson().ToJsonString();
        Check(data.RewriteMetadata()==0 && data.RewriteScalars((_,_,text)=>text)==0 && data.ToJson().ToJsonString()==before,"No-op metadata and scalar editors preserve the document");
        var copy=data.Clone();Check(copy.RewriteMetadata(_=>"New",value=>value+1)==2,"Tag and layer metadata count separately");
        Check(data.ToJson().ToJsonString()==before && copy.ToJson()[0]!["Tag"]!.GetValue<string>()=="New","Detached clone edits do not mutate the source");
        Check(copy.RewriteMetadata(_=>"New",value=>value)==0,"Unchanged mappings do not count edits");
        Throws<ArgumentNullException>(()=>data.RewriteScalars(null!),"Null scalar mapper is rejected");
        Throws<ArgumentNullException>(()=>data.Clone().RewriteMetadata(_=>null!),"Null tag result is rejected");
        Throws<ArgumentNullException>(()=>data.Clone().RewriteScalars((_,_,_)=>null!),"Null scalar result is rejected");
        Throws<InvalidOperationException>(()=>default(SerializedHierarchy).RewriteMetadata(),"Uninitialized hierarchy is rejected");
        Throws<InvalidOperationException>(()=>default(SceneDocumentSnapshot).ToYaml(),"Uninitialized scene handle is rejected");
        var visited=new List<(string,string?)>();
        var changes=copy.RewriteScalars((name,type,text)=>{visited.Add((name,type));return name.Equals("sortingLayer",StringComparison.OrdinalIgnoreCase)?"9":text;});
        Check(changes==2,"Legacy and typed representations count the same member once per component");
        Check(visited.Any(x=>x.Item2==null) && visited.Any(x=>x.Item2=="System.UInt64"),"Mapper receives legacy and typed scalar information");
        Check(visited.All(x=>x.Item1 is not ("nestedLayerMask" or "referenceLayerMask" or "arrayLayerMask")),"Objects, arrays and references are not scalar rewrites");
        Check(copy.ToJson()[0]!["Components"]![0]!["Graph"]!["Members"]![5]!["Value"]!["Members"]![0]!["Value"]!["Value"]!.GetValue<string>()=="2","Nested scalar is unchanged");
        using var scene=new Scene("Detached document");var go=scene.CreateGameObject("owner");
        var yaml=SceneAssetDocuments.Serialize(scene);var document=SceneDocumentSnapshot.Parse(yaml);
        document.Objects.RewriteMetadata(_=>"UnknownTag",_=>17);
        Check(go.tag=="Untagged" && go.layer!=17,"Parsing and editing a scene does not activate its live objects");
        var roundtrip=SceneDocumentSnapshot.Parse(document.ToYaml());
        Check(roundtrip.Id==document.Id && roundtrip.Name==scene.name && roundtrip.Objects.ToJson().ToJsonString()==document.Objects.ToJson().ToJsonString(),"Scene metadata, object identities and payload round-trip");
        var reference=PrefabReferenceSnapshot.Create("guid:"+Guid.NewGuid().ToString("N"),go.Id,new Dictionary<Guid,Guid>{{Guid.NewGuid(),go.Id}});reference.SetBaseline(Sample());document.References.Add(reference);
        var withRef=SceneDocumentSnapshot.Parse(document.ToYaml());
        Check(withRef.References.Count==1 && withRef.References.Find(go.Id)!.Value.Asset==reference.Asset && withRef.References.Find(go.Id)!.Value.ObjectIds.SequenceEqual(reference.ObjectIds),"Scene references and identity map survive encoding");
        Check(withRef.References.Find(go.Id)!.Value.Baseline.RewriteMetadata(_=>"Inherited")==1 && document.References.Find(go.Id)!.Value.Baseline.ToJson()[0]!["Tag"]!.GetValue<string>()=="Old","Parsed inherited snapshots are detached from the original document");
        var unknown=SceneDocumentSnapshot.Parse(yaml);var raw=typeof(SceneDocumentSnapshot).GetField("_document",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(unknown)!;
        raw.GetType().GetProperty("GameObjects")!.SetValue(raw,typeof(SerializedHierarchy).GetProperty("Data",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Sample()));
        var unresolved=SceneDocumentSnapshot.Parse(unknown.ToYaml());
        Check(unresolved.Objects.ToJson()[0]!["Components"]![0]!["Type"]!.GetValue<string>()=="Absent.Component, Absent.Assembly","Document parse preserves unavailable component types without resolving them");
        for(var i=0;i<32;i++){
            var h=Sample(i);var source=h.ToJson().ToJsonString();var clone=h.Clone();
            clone.RewriteMetadata(tag=>tag+"!",layer=>layer^3);clone.RewriteScalars((_,_,text)=>text);
            Check(h.ToJson().ToJsonString()==source && clone.CollectObjectIds().SetEquals(h.CollectObjectIds()),"Clone preserves source and identity set "+i);
        }
    }
    static string Baseline(Scene scene,GameObject root)=>SceneDocumentSnapshot.Parse(SceneAssetDocuments.Serialize(scene)).References.Find(root.Id)!.Value.Baseline.ToJson().ToJsonString();
    void Baselines()
    {
        using var scene=new Scene("Baselines");var source=scene.CreateGameObject("Source");var child=scene.CreateGameObject("Child");child.transform.SetParent(source.transform);
        var prefab=PrefabDocumentSnapshot.Capture(source).CreateAsset("memory.prefab.yaml");
        try{
            var root=PrefabAssetOperations.Instantiate(prefab,scene);var initial=Baseline(scene,root);var objects=new[]{root,root.transform.children.Single().gameObject,root};
            SerializedHierarchy retained=default;var calls=0;
            var edits=PrefabBaselineChange.Prepare(objects,h=>{calls++;retained=h;return h.RewriteMetadata(_=>"Changed")>0;});
            Check(calls==1 && edits.Count==1 && Baseline(scene,root)==initial,"Preparation deduplicates an instance and does not publish");
            Throws<NotSupportedException>(()=>((IList)edits).Clear(),"Prepared edit list is read-only");
            retained.RewriteMetadata(_=>"Retained before apply");edits[0].Apply();var changed=Baseline(scene,root);
            Check(changed.Contains("Changed") && !changed.Contains("Retained before apply") && root.tag=="Untagged","Publication clones callback data and leaves live object policy to caller");
            retained.RewriteMetadata(_=>"Retained after apply");Check(Baseline(scene,root)==changed,"Retained callback data cannot mutate an applied baseline");
            edits[0].Apply();Check(Baseline(scene,root)==changed,"Apply is idempotent while current");
            edits[0].Rollback();edits[0].Rollback();Check(Baseline(scene,root)==initial,"Rollback restores the original inherited snapshot and is idempotent");
            edits[0].Apply();edits[0].Rollback();Check(Baseline(scene,root)==initial,"Prepared edit can be reapplied after rollback");
            Check(PrefabBaselineChange.Prepare(objects,h=>{h.RewriteMetadata(_=>"Discard");return false;}).Count==0 && Baseline(scene,root)==initial,"Declined edits discard detached modifications");
            Throws<IOException>(()=>PrefabBaselineChange.Prepare(objects,h=>{h.RewriteMetadata(_=>"Failure");throw new IOException();}),"Callback failure leaves no publication");
            Check(Baseline(scene,root)==initial,"Callback exception preserves the baseline");
            var a=PrefabBaselineChange.Prepare(objects,h=>h.RewriteMetadata(_=>"A")>0)[0];var b=PrefabBaselineChange.Prepare(objects,h=>h.RewriteMetadata(_=>"B")>0)[0];
            a.Apply();Throws<InvalidOperationException>(b.Apply,"Stale prepared edit cannot overwrite a newer baseline");a.Rollback();
            a.Apply();var c=PrefabBaselineChange.Prepare(objects,h=>h.RewriteMetadata(_=>"C")>0)[0];c.Apply();
            Throws<InvalidOperationException>(a.Rollback,"Stale rollback cannot overwrite another edit");c.Rollback();a.Rollback();
            Check(Baseline(scene,root)==initial,"Ordered rollbacks preserve baseline identity");
            PrefabBaselineChange? nested=null;
            Throws<InvalidOperationException>(()=>PrefabBaselineChange.Prepare(objects,h=>{nested=PrefabBaselineChange.Prepare(objects,n=>n.RewriteMetadata(_=>"Nested")>0)[0];nested.Apply();return h.RewriteMetadata(_=>"Outer")>0;}),"Reentrant baseline replacement during preparation is detected");
            Check(Baseline(scene,root).Contains("Nested"),"Preparation conflict does not overwrite the reentrant edit");nested!.Rollback();
            var root2=PrefabAssetOperations.Instantiate(prefab,scene);var second=Baseline(scene,root2);var n=0;
            Throws<IOException>(()=>PrefabBaselineChange.Prepare(new[]{root,root2},h=>{if(++n==2)throw new IOException();return h.RewriteMetadata(_=>"Batch")>0;}),"Second callback failure aborts a multi-instance preparation");
            Check(Baseline(scene,root)==initial && Baseline(scene,root2)==second,"Batch preparation failure leaves both instances unchanged");
            var batch=PrefabBaselineChange.Prepare(new[]{root,root2},h=>h.RewriteMetadata(layers:l=>l+1)>0);
            Check(batch.Count==2,"Distinct connected references receive distinct prepared changes");foreach(var edit in batch)edit.Apply();foreach(var edit in batch.Reverse())edit.Rollback();
            Check(Baseline(scene,root)==initial && Baseline(scene,root2)==second,"Multi-instance apply and rollback preserve full snapshots");
            Check(PrefabBaselineChange.Prepare(new[]{source},_=>true).Count==0,"Unconnected objects are ignored");
            Throws<ArgumentNullException>(()=>PrefabBaselineChange.Prepare(null!,_=>true),"Null object sequence is rejected");Throws<ArgumentNullException>(()=>PrefabBaselineChange.Prepare(objects,null!),"Null baseline editor is rejected");Throws<ArgumentNullException>(()=>PrefabBaselineChange.Prepare(new GameObject[]{null!},_=>true),"Null sequence item is rejected");
        }finally{BObject.DestroyImmediate(prefab);}
    }
    void Workbench()
    {
        DotNetEditorPlatform.Initialize();
        var files=DispatchProxy.Create<IFileSystem,FileProbe>();var fs=(FileProbe)files;
        using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().AddSingleton<IFileSystem>(files).BuildServiceProvider();
        PlatformRuntime.Initialize(services);using var scope=EditorServiceScope.Attach(services);
        var current=Assembly.Load("BEngine.EditorWorkbench.Editor");var applier=current.GetType("BEngine.Editor.ProjectTagLayerSettingsApplier",true)!;
        var legacyPath=Path.Combine(legacy,"BEngine.EditorWorkbench.Editor/release/BEngine.EditorWorkbench.Editor.dll");
        var context=new LegacyContext();var old=context.LoadFromAssemblyPath(legacyPath);var prior=old.GetType(applier.FullName!,true)!;
        legacyInputs.Add(new{Path=legacyPath,SHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacyPath)))});
        var documentType=applier.GetNestedType("DocumentEdit",BindingFlags.NonPublic)!;
        for(var i=0;i<96;i++){
            using var scene=new Scene("Parity "+i);scene.CreateGameObject("Source");
            var doc=SceneDocumentSnapshot.Parse(SceneAssetDocuments.Serialize(scene));var raw=typeof(SceneDocumentSnapshot).GetField("_document",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(doc)!;
            raw.GetType().GetProperty("GameObjects")!.SetValue(raw,typeof(SerializedHierarchy).GetProperty("Data",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Sample(i)));
            var root=doc.Objects.ToJson()[0]!["Id"]!.GetValue<Guid>();var reference=PrefabReferenceSnapshot.Create("guid:missing",root,new Dictionary<Guid,Guid>());reference.SetBaseline(Sample(i+1));doc.References.Add(reference);
            var yaml=doc.ToYaml();var previous=SceneDocumentSnapshot.Parse(yaml);var next=SceneDocumentSnapshot.Parse(yaml);
            object oldData=typeof(SceneDocumentSnapshot).GetField("_document",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(previous)!;
            var edit=Activator.CreateInstance(documentType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object?[]{next.Objects,next.References,null,(Func<string>)next.ToYaml},null)!;
            var tags=new Dictionary<string,string>{{"Old",i%3==0?"Untagged":"Renamed"}};var layers=new Dictionary<ulong,ulong>{{1,2},{2,4},{4,(ulong)(i%5+1)}};
            var beforeCount=(int)Invoke(prior,"RewriteAssetTags",null,oldData,tags)!+(int)Invoke(prior,"RewriteAssetLayers",null,oldData,layers)!;
            var afterCount=(int)Invoke(applier,"RewriteAssetTags",null,edit,tags)!+(int)Invoke(applier,"RewriteAssetLayers",null,edit,layers)!;
            Check(beforeCount==afterCount && previous.ToYaml()==next.ToYaml(),"Actual previous/new scene rewrite count and YAML parity "+i);parity++;
            var prefab=PrefabDocumentSnapshot.Parse(yaml.Replace("BEngine.Scene","BEngine.Prefab"));prefab.SetObjects(Sample(i));prefab.SetBase(reference);
            var oldPrefab=PrefabDocumentSnapshot.Parse(prefab.ToYaml());var newPrefab=PrefabDocumentSnapshot.Parse(prefab.ToYaml());
            var data=typeof(PrefabDocumentSnapshot).GetProperty("Data",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(oldPrefab)!;
            edit=Activator.CreateInstance(documentType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object?[]{newPrefab.Objects,newPrefab.References,newPrefab.BaseReference,(Func<string>)newPrefab.ToYaml},null)!;
            beforeCount=(int)Invoke(prior,"RewriteAssetTags",null,data,tags)!+(int)Invoke(prior,"RewriteAssetLayers",null,data,layers)!;
            afterCount=(int)Invoke(applier,"RewriteAssetTags",null,edit,tags)!+(int)Invoke(applier,"RewriteAssetLayers",null,edit,layers)!;
            Check(beforeCount==afterCount && oldPrefab.ToYaml()==newPrefab.ToYaml(),"Actual previous/new prefab and inherited baseline parity "+i);parity++;
        }
        var hostAssembly=typeof(EditorProjectSettings).Assembly;var bridge=hostAssembly.GetType("BEngine.Editor.EditorBridge",true)!;
        var host=DispatchProxy.Create(hostAssembly.GetType("BEngine.Editor.IEditorHost",true)!,typeof(HostProbe));var proxy=(HostProbe)host;
        proxy.Root=Path.Combine(output,"Project-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(proxy.Root,"Assets"));Invoke(bridge,"Attach",null,host);
        try{
            for(var scenario=0;scenario<4;scenario++)Transaction(scenario,applier,current,proxy,fs);
        }finally{Invoke(bridge,"Detach",null,host);context.Unload();}
    }
    void Transaction(int scenario,Type applier,Assembly workbench,HostProbe host,FileProbe fs)
    {
        host.Assets.Clear();host.Imported.Clear();host.Repaints=0;
        var folder=Path.Combine(host.Root,"Assets","Case"+scenario);Directory.CreateDirectory(folder);var settings=Path.Combine(folder,"ProjectSettings.yaml");
        EditorProjectSettings.Initialize(settings);EditorProjectSettings.current.Tags=["Untagged","Old"];
        EditorProjectSettings.current.SortingLayers.Add(new SortingLayerData{Value=(ulong)EditorProjectSettings.current.SortingLayers.Count+1,Name="Custom"});EditorProjectSettings.Save();
        using var scene=new Scene("Transaction");var source=scene.CreateGameObject("Source");source.tag="Old";
        var prefab=PrefabDocumentSnapshot.Capture(source).CreateAsset("transaction.prefab.yaml");
        try{
            var instance=PrefabAssetOperations.Instantiate(prefab,scene);host.Scenes=[scene];
            var sourceText=SceneAssetDocuments.Serialize(scene);var sourcePath=Path.Combine(folder,"source.scene.yaml");File.WriteAllText(sourcePath,sourceText);
            var assetId=Guid.NewGuid();host.Assets.Add(new EditorAssetInfo(assetId,"Assets/Case"+scenario+"/source.scene.yaml",sourcePath,sourcePath,"Scene",false,null,0));
            var priorSettings=File.ReadAllText(settings);var priorBaseline=Baseline(scene,instance);
            var draftType=workbench.GetType("BEngine.Editor.TagLayerSettingsDraft",true)!;var draft=Activator.CreateInstance(draftType,true)!;Invoke(draftType,"Reload",draft);Invoke(draftType,"RenameTag",draft,1,"New");
            if(scenario==3){sourceText="invalid: [";File.WriteAllText(sourcePath,sourceText);}
            if(scenario==1)fs.FailOnce=(method,args)=>method=="WriteAllText" && (string)args[0]! == settings;
            if(scenario==2)fs.FailOnce=(method,args)=>method=="MoveFile" && (string)args[1]! == sourcePath;
            if(scenario==0){
                Invoke(applier,"Apply",null,draft);
                Check(source.tag=="New" && instance.tag=="New" && Baseline(scene,instance).Contains("New"),"Successful transaction updates live tags and inherited baseline");
                Check(File.ReadAllText(sourcePath).Contains("New") && EditorProjectSettings.current.Tags.Contains("New") && !EditorProjectSettings.current.Tags.Contains("Old"),"Successful transaction saves document and project settings");
                Check(host.Imported.Count==1 && host.Repaints==1,"Successful migration reimports the changed asset and repaints once");
            }else{
                if(scenario==3)Throws<InvalidOperationException>(()=>Invoke(applier,"Apply",null,draft),"Invalid document preparation is rejected");
                else Throws<IOException>(()=>Invoke(applier,"Apply",null,draft),"Injected filesystem failure is propagated "+scenario);
                Check((scenario==3 || fs.FailOnce is null) && File.ReadAllText(sourcePath)==sourceText && File.ReadAllText(settings)==priorSettings,"Targeted transaction failure restores exact document and settings bytes "+scenario);
                Check(source.tag=="Old" && instance.tag=="Old" && Baseline(scene,instance)==priorBaseline && EditorProjectSettings.current.Tags.Contains("Old"),"Transaction failure preserves live values and prefab baseline "+scenario);
                Check(host.Imported.Count==0 && host.Repaints==0 && !Directory.EnumerateFiles(folder,"*.tmp").Any(),"Failed transaction does not reimport or leak staging files "+scenario);
            }
        }finally{host.Scenes=[];fs.FailOnce=null;BObject.DestroyImmediate(prefab);}
    }
}

sealed class LegacyContext():AssemblyLoadContext(isCollectible:true)
{protected override Assembly? Load(AssemblyName name)=>Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==name.Name);}
public class FileProbe:DispatchProxy
{
    public Func<string,object?[],bool>? FailOnce;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(FailOnce?.Invoke(method!.Name,args!)==true){FailOnce=null;throw new IOException("Injected filesystem failure");}
        try{return method!.Invoke(PhysicalFileSystem.Shared,args);}catch(TargetInvocationException ex) when(ex.InnerException is {} inner){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();throw;}
    }
}
public class HostProbe:DispatchProxy
{
    public string Root="";public IReadOnlyList<Scene> Scenes=[];public List<EditorAssetInfo> Assets=[];public List<string> Imported=[];public int Repaints;
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch{
        "get_ProjectRootPath"=>Root,"get_AssetsRootPath"=>Path.Combine(Root,"Assets"),"get_OpenScenes"=>Scenes,
        "FindAssets"=>Assets.ToArray(),"GetAsset"=>Assets.Where(a=>args![0] is Guid guid?a.Guid==guid:a.AssetPath==(string)args[0]!).Select(a=>(EditorAssetInfo?)a).FirstOrDefault(),
        "ImportAsset"=>Import((string)args![0]!),"RepaintAllWindows"=>Repaint(),
        _=>method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null};
    object? Import(string path){Imported.Add(path);return null;}object? Repaint(){Repaints++;return null;}
}
