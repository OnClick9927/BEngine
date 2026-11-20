using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

new Probe(args[0], Path.GetFullPath(args[1])).Run();

sealed class Probe(string mode, string output)
{
    readonly List<string> checks = [];
    void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
    void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { Check(true,name); return; } throw new Exception(name); }
    static T Worker<T>(Func<T> action) => Task.Run(action).GetAwaiter().GetResult();
    static bool Rejected<T>(Action action) where T:Exception
    { try { action(); return false; } catch(T) { return true; } }

    public void Run()
    {
        Directory.CreateDirectory(output);
        Check(!typeof(ProjectScriptHostSession).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Any(a=>a.AssemblyName.Split(',')[0]==GetType().Assembly.GetName().Name),"Consumer has no Core friend access");
        if(mode=="Trace")Trace(); else if(mode=="Commands")Commands(); else if(mode=="Inputs")Inputs(); else if(mode=="Assets")Assets(); else throw new ArgumentException(mode);
        File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("STARTUP_COMMAND_BOUNDARY_OK|"+mode+"|"+checks.Count);
    }

    void Trace()
    {
        Check(typeof(EditorStartupTrace).Assembly==typeof(ProjectScriptHostSession).Assembly,"Neutral startup recording belongs to Core Editor");
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories);
        Throws<ArgumentException>(()=>EditorStartupTrace.Begin(""),"Empty initial stages are rejected");
        using var trace=EditorStartupTrace.Begin("bootstrap");
        Throws<InvalidOperationException>(()=>EditorStartupTrace.Begin("nested"),"Only one startup trace may be active");
        Throws<ArgumentException>(()=>trace.Mark(" "),"Empty stage labels are rejected");
        Throws<ArgumentException>(()=>trace.Measure(" "),"Empty detail labels are rejected");
        Check(Worker(()=>Rejected<InvalidOperationException>(()=>trace.Mark("worker"))),"Workers cannot change stages");
        Check(Worker(()=>Rejected<InvalidOperationException>(()=>trace.Complete())),"Workers cannot complete the trace");
        Check(Worker(()=>Rejected<InvalidOperationException>(trace.Dispose)),"Workers cannot dispose the trace");
        var detail=trace.Measure("owner");detail.Dispose();detail.Dispose();
        var late=trace.Measure("late");
        Task.WaitAll(Enumerable.Range(0,24).Select(i=>Task.Run(()=>{using var measurement=trace.Measure("worker-"+i);})).ToArray());
        trace.Mark("assets");
        using(trace.Measure("assets")){}
        var snapshot=trace.Complete();
        Check(snapshot.Stages.Select(s=>s.Name).SequenceEqual(["bootstrap","assets"]),"Stages preserve owner labels and order");
        Check(snapshot.Details.Count==26&&snapshot.Details.Count(s=>s.Name=="owner")==1,"Concurrent details are complete and repeated disposal is idempotent");
        Check(snapshot.Stages.All(s=>s.StartMilliseconds>=0&&s.Milliseconds>=0)&&snapshot.Details.All(s=>s.StartMilliseconds>=0&&s.Milliseconds>=0),"Durations and offsets are nonnegative");
        Check(Math.Abs(snapshot.Stages.Sum(s=>s.Milliseconds)-snapshot.ManagedStartupMilliseconds)<0.001,"Contiguous stages account for the complete managed startup duration");
        Check(ReferenceEquals(snapshot,trace.Complete()),"Repeated completion returns the same immutable snapshot");
        Throws<NotSupportedException>(()=>((IList<EditorStartupTiming>)snapshot.Stages).Add(new("external",0,0)),"Stage snapshots are read-only");
        Throws<NotSupportedException>(()=>((IList<EditorStartupTiming>)snapshot.Details).Clear(),"Detail snapshots are read-only");
        Throws<ObjectDisposedException>(()=>trace.Mark("late"),"Completed traces reject stage mutation");
        Throws<ObjectDisposedException>(()=>trace.Measure("late"),"Completed traces reject new detail scopes");
        using(var next=EditorStartupTrace.Begin("next"))
        {
            late.Dispose();trace.Dispose();
            using(next.Measure("current")){}
            var result=next.Complete();
            Check(result.Details.Select(s=>s.Name).SequenceEqual(["current"]),"Retired scopes cannot write into a newer trace");
        }
        Check(snapshot.Details.Count==26,"Late completion cannot mutate an already published snapshot");
        using(var abandoned=EditorStartupTrace.Begin("abandoned")){}
        using(var retry=EditorStartupTrace.Begin("retry"))
            Check(retry.Complete().Stages.Single().Name=="retry","Abandoned startup releases recording ownership");
        var deleted=EditorStartupTrace.Begin("disposed");deleted.Dispose();deleted.Dispose();
        Throws<ObjectDisposedException>(()=>deleted.Complete(),"Disposal without completion does not invent a successful profile");
        Check(Directory.GetFiles(output,"*",SearchOption.AllDirectories).SequenceEqual(files),"Core recording creates no profile directory or file");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.EditorWorkbench.Host" or "BEngine.IMGUI.Editor" or "BEngine.Graphics"),"Core tracing loads no workbench, GUI or graphics implementation");
    }

    void Inputs()
    {
        Check(typeof(ProjectCompilationInputs).Assembly==typeof(ProjectScriptHostSession).Assembly,"Compiler input conventions belong to Core");
        Check(ProjectCompilationInputs.IsScriptPath("Assets/test.cs")&&ProjectCompilationInputs.IsScriptPath("Assets/TEST.CS"),"CSharp inputs use case-insensitive extension matching");
        Check(ProjectCompilationInputs.IsScriptPath("Assets/Runtime.asmdef.yaml")&&ProjectCompilationInputs.IsScriptPath("Assets/EDITOR.ASMDEF.YAML"),"Assembly definition edits are compiler inputs");
        Check(!ProjectCompilationInputs.IsScriptPath("Assets/test.cs.meta")&&!ProjectCompilationInputs.IsScriptPath("Assets/test.yaml")&&!ProjectCompilationInputs.IsScriptPath("Assets/test.frag.cg")&&!ProjectCompilationInputs.IsScriptPath(""),"Content, metadata and shader formats remain outside script classification");
        Throws<ArgumentNullException>(()=>ProjectCompilationInputs.IsScriptPath(null!),"Null paths are rejected explicitly");
        Check(!default(GameViewFrameTimingSnapshot).HasValue&&new GameViewFrameTimingSnapshot(60,16.6,1).HasValue,"Frame snapshot validity is a public value contract");
        Check(typeof(GameViewFrameTimingSnapshot).Assembly.GetType("BEngine.Editor.GameViewFrameTiming") is null,"Core owns no native window smoothing implementation");
        var type=Assembly.Load("BEngine.Windows.Editor").GetType("BEngine.Editor.GameViewFrameTiming",true)!;
        var timing=Activator.CreateInstance(type,true)!;
        var record=type.GetMethod("RecordSample",BindingFlags.Instance|BindingFlags.NonPublic)!;
        GameViewFrameTimingSnapshot Snapshot()=>(GameViewFrameTimingSnapshot)type.GetProperty("snapshot",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(timing)!;
        Check(!Snapshot().HasValue,"Native window sampling starts empty");
        foreach(var invalid in new[]{double.NaN,double.PositiveInfinity,0d,-1d})record.Invoke(timing,[invalid]);
        Check(!Snapshot().HasValue,"Invalid native intervals are ignored");
        record.Invoke(timing,[1d/60d]);var first=Snapshot();
        Check(first.SampleCount==1&&Math.Abs(first.FramesPerSecond-60)<0.0001,"Native first sample has the original exact timing");
        record.Invoke(timing,[1d/30d]);var second=Snapshot();
        Check(second.SampleCount==2&&Math.Abs(second.FrameTimeMilliseconds-(1000d/60d*0.9+1000d/30d*0.1))<0.0001,"Platform smoothing preserves the existing algorithm");
    }

    void Assets()
    {
        var root=Path.Combine("E:/Project/_BZP/BEngine/Temp/SC43",Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root,"Assets"));
        File.WriteAllText(Path.Combine(root,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: Asset Source Ownership\n");
        DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
        using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
        PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
        var workspace=ProjectWorkspace.Open(root);
        var database=new BEngine.ProjectSystem.Editor.AssetDatabase(workspace);
        Check(database.GetType().Assembly.GetName().Name=="BEngine.Core.Editor","Source transactions belong to the asset pipeline");
        var original=Path.Combine(root,"Assets/original.txt");File.WriteAllText(original,"asset content");
        database.Refresh();var guid=database.AssetPathToGuid("Assets/original.txt");
        Check(guid is not null&&File.Exists(original+".meta"),"Refresh creates the original metadata identity");
        var stale=database.PrepareRefresh();
        Check(database.CopyAssetSource("Assets/original.txt","Assets/copied.txt")=="","The database copies project-relative source paths");
        Check(!File.Exists(Path.Combine(root,"Assets/copied.txt.meta")),"Copy does not duplicate source metadata");
        Check(!database.TryApplyRefresh(stale,out _),"Source edits invalidate earlier background refresh snapshots");
        Check(database.AssetPathToGuid("Assets/copied.txt") is null,"Source mutation leaves refresh and notification timing to the caller");
        database.Refresh();var copiedGuid=database.AssetPathToGuid("Assets/copied.txt");
        Check(copiedGuid is not null&&copiedGuid!=guid,"Copied assets receive independent GUIDs");
        Check(database.MoveAssetSource("Assets/copied.txt","Assets/moved.txt")=="","The database moves source and metadata together");
        database.Refresh();
        Check(database.AssetPathToGuid("Assets/moved.txt")==copiedGuid&&database.AssetPathToGuid("Assets/copied.txt") is null,"Moving preserves GUID and updates its path after refresh");
        Check(!File.Exists(Path.Combine(root,"Assets/copied.txt.meta"))&&File.Exists(Path.Combine(root,"Assets/moved.txt.meta")),"Moved metadata has no leftover original file");
        var external=Path.Combine(root,"external.txt");File.WriteAllText(external,"external");
        Check(database.CopyAssetSource(external,"Assets/imported.txt")==""&&File.ReadAllText(Path.Combine(root,"Assets/imported.txt"))=="external","External files can be imported into project Assets");
        Check(database.MoveAssetSource(external,"Assets/not-moved.txt").Length>0&&File.Exists(external),"Move cannot take ownership of external files");
        var outside=Path.Combine(root,"outside.txt");
        Check(database.CopyAssetSource(original,outside).Length>0&&!File.Exists(outside),"Copies cannot write outside project Assets");
        Check(database.MoveAssetSource("Assets/original.txt","Assets/../escape.txt").Length>0&&File.Exists(original),"Move rejects traversal outside Assets");
        Check(database.CopyAssetSource(original,"Assets/moved.txt").Length>0&&File.ReadAllText(Path.Combine(root,"Assets/moved.txt"))=="asset content","Copy cannot overwrite an existing asset");
        var orphan=Path.Combine(root,"Assets/orphan.txt.meta");File.WriteAllText(orphan,"orphan identity");
        Check(database.CopyAssetSource(original,"Assets/orphan.txt").Length>0&&!File.Exists(Path.Combine(root,"Assets/orphan.txt")),"Copy rejects an orphan destination identity");
        Check(database.MoveAssetSource(external,"Assets/orphan.txt").Length>0&&File.Exists(external),"Rejected moves preserve their source");
        var sourceFolder=Path.Combine(root,"Assets/Folder");Directory.CreateDirectory(sourceFolder);File.WriteAllText(Path.Combine(sourceFolder,"inside.txt"),"nested");
        database.Refresh();var nestedGuid=database.AssetPathToGuid("Assets/Folder/inside.txt");
        Check(database.CopyAssetSource("Assets/Folder","Assets/Folder Copy")=="","Folder source copies preserve nested content");
        Check(!Directory.GetFiles(Path.Combine(root,"Assets/Folder Copy"),"*.meta",SearchOption.AllDirectories).Any(),"Folder copies exclude all nested metadata");
        database.Refresh();Check(database.AssetPathToGuid("Assets/Folder Copy/inside.txt")!=nestedGuid,"Copied child assets get new identities");
        Check(database.MoveAssetSource("Assets/Folder","Assets/Folder Moved")=="","Folders move through the same source transaction");
        database.Refresh();Check(database.AssetPathToGuid("Assets/Folder Moved/inside.txt")==nestedGuid,"Moving folders preserves nested asset identities");
        Check(database.CopyAssetSource("Assets/Folder Moved","Assets/Folder Moved/Recursive").Length>0&&!Directory.Exists(Path.Combine(root,"Assets/Folder Moved/Recursive")),"A folder cannot be copied recursively into itself");
        Check(database.MoveAssetSource("Assets/Folder Moved","Assets/Folder Moved/Recursive").Length>0&&Directory.Exists(Path.Combine(root,"Assets/Folder Moved")),"A folder cannot be moved into itself");
        Check(database.CopyAssetSource("Assets/missing.txt","Assets/missing-copy.txt").Length>0,"Missing sources return an asset operation error");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.EditorWorkbench.Host" or "BEngine.IMGUI.Editor" or "BEngine.Application"),"Asset source editing needs no default shell, IMGUI or game framework");
        File.WriteAllText(Path.Combine(output,"Fixture.txt"),root);
    }

    void Commands()
    {
        var root=Path.Combine("E:/Project/_BZP/BEngine/Temp/SC43",Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root,"Assets"));Directory.CreateDirectory(Path.Combine(root,"Packages"));
        File.WriteAllText(Path.Combine(root,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: Command Ownership\n");
        File.WriteAllText(Path.Combine(root,"Packages/manifest.yaml"),"packages: []\n");
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",root);
        Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH",Path.Combine(root,"Hub/Packages"));
        DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
        using var defaults=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().AddBEngineEditorCompilation().BuildServiceProvider();
        var factory=new TrackingFactory(defaults.GetRequiredService<IProjectPackageManagerFactory>());
        using var services=new ServiceCollection().AddSingleton<IProjectPackageManagerFactory>(factory)
            .AddSingleton<IPackageCatalogSource>(new EmptyCatalog()).AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
            .AddBEngineEditorCompilation().AddBEngineEditorCompilation().BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
        PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
        var runner=services.GetRequiredService<IEditorCommandLineRunner>();
        Check(typeof(IEditorCommandLineRunner).Assembly==typeof(ProjectScriptHostSession).Assembly,"Package command activation belongs to Core Editor");
        Check(ReferenceEquals(runner,services.GetRequiredService<IEditorCommandLineRunner>()),"Command registration is idempotent and stateless per host");
        var custom=new CustomRunner();using(var overrides=new ServiceCollection().AddSingleton<IEditorCommandLineRunner>(custom).AddBEngineEditorCompilation().BuildServiceProvider())
            Check(ReferenceEquals(custom,overrides.GetRequiredService<IEditorCommandLineRunner>()),"A host can replace command dispatch without a workbench");
        var empty=new BPackageCatalog([]);
        Throws<ArgumentNullException>(()=>runner.Run(null!,empty),"Null command input is rejected");
        Throws<ArgumentException>(()=>runner.Run([],empty),"Empty command input is rejected");
        Throws<ArgumentException>(()=>runner.Run(["project"],empty),"Dispatch requires an explicit option");
        Throws<ArgumentException>(()=>runner.Run(["--fixture",null!],empty),"Null argument elements are rejected");
        Throws<ArgumentException>(()=>runner.Run(["--missing"],empty),"Unknown commands do not activate packages");
        var definition=Path.Combine(root,"Hub/Packages/Fixture/Fixture-1.0.0/package.yaml");Directory.CreateDirectory(Path.GetDirectoryName(definition)!);
        new PackageDefinitionDocument{Id="com.fixture.commands",DisplayName="Command Fixture",MinimumEngineVersion=EngineVersion.Current,CommandLineOptions=["--fixture"],Editor=new(){Assembly="SC43.Tools",RootNamespace="SC43"}}.Save(definition);
        var folder=Path.Combine(Path.GetDirectoryName(definition)!,"SC43.Tools");Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory,"CommandFixture.cs"),Path.Combine(folder,"Command.cs"));
        File.WriteAllText(Path.Combine(folder,"SC43.Tools.asmdef.yaml"),YamlUtility.Serialize(new AssemblyDefinitionDocument{Name="SC43.Tools",RootNamespace="SC43",EditorOnly=true}));
        var catalog=new BPackageCatalog([definition]);
        Throws<ArgumentException>(()=>runner.Run(["--fixture","--project"],catalog),"Missing project values fail before activation");
        Throws<FileNotFoundException>(()=>runner.Run(["--fixture","--project",Path.Combine(root,"missing")],catalog),"Invalid project roots fail before activation");
        Check(factory.Calls==0,"Validation errors do not invoke the package factory");
        using(var active=EditorInstanceContext.Create(root))
            Throws<InvalidOperationException>(()=>runner.Run(["--fixture"],catalog),"Command dispatch cannot replace a live project instance");
        var events=Path.Combine(root,"events.txt");Environment.SetEnvironmentVariable("SC43_EVENTS",events);
        var names=new[]{"TEMP","TMP","TMPDIR",BEngine.Tooling.ToolEnvironment.ProjectDirectoryVariable};
        var before=names.ToDictionary(n=>n,Environment.GetEnvironmentVariable);
        RuntimePackageState.SetEnabled("com.fixture.commands",false);
        void Run(string operation)
        {
            File.WriteAllText(events,"");Environment.SetEnvironmentVariable("SC43_MODE",operation);
            if(operation is "fail" or "cleanup" or "optionfail" or "duplicate")
                Throws<InvalidOperationException>(()=>runner.Run(["--fixture","--project",root],catalog),operation+" propagates the command failure");
            else if(operation=="both")
            {
                try{runner.Run(["--fixture","--project",root],catalog);throw new Exception("Expected aggregate");}
                catch(AggregateException error){Check(error.Flatten().InnerExceptions.Count==2,"Execution and cleanup failures are both retained");}
            }
            else Check(runner.Run(["--FIXTURE","--project",root],catalog)==37,operation+" returns the command exit code");
            Check(names.All(n=>Environment.GetEnvironmentVariable(n)==before[n]),operation+" restores process environment");
            Check(!RuntimePackageState.IsEnabled("com.fixture.commands"),operation+" restores the previous runtime package state");
            var log=File.ReadAllLines(events);
            Check(log.Count(e=>e=="dispose-main")==1&&log.Count(e=>e=="dispose-other")==1,operation+" disposes all constructed command instances once");
            Check(log.ToList().IndexOf("dispose-other")<log.ToList().IndexOf("dispose-main"),operation+" retires commands in reverse activation order");
            Check(!RuntimeTypeCache.GetAllTypes().Any(t=>t.FullName=="SC43.Command"),operation+" retires command package types");
            var remnants=Directory.EnumerateFiles(Path.Combine(root,"Temp/PackageTools"),"*",SearchOption.AllDirectories).ToArray();
            Check(remnants.All(p=>p.Replace('\\','/').Contains("/Library/PackageImages/",StringComparison.Ordinal)),operation+" retires temporary project data; only leased images may await collection");
            if(operation=="readonly")Check(log.Contains("readonly"),"Tools receive a read-only argument snapshot");
            if(operation=="reenter")Check(log.Contains("reentry-rejected"),"Nested command activation is rejected before global state changes");
        }
        foreach(var operation in new[]{"success","readonly","reenter","fail","cleanup","both","optionfail","duplicate","recovery"})Run(operation);
        Check(factory.Calls==9,"Every invocation uses the registered package factory");
        Check(UnrelatedTool.Constructions==0,"Unrelated loaded command classes are never instantiated");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.EditorWorkbench.Host" or "BEngine.Application" or "BEngine.IMGUI.Editor" or "BEngine.Graphics.Editor"),"Command dispatch needs no workbench, game framework, IMGUI or shader backend");
        File.WriteAllText(Path.Combine(output,"Fixture.txt"),root);
    }
}

sealed class TrackingFactory(IProjectPackageManagerFactory inner):IProjectPackageManagerFactory
{public int Calls;public BPackageManager Create(ProjectWorkspace workspace,BPackageCatalog? catalog=null){Calls++;return inner.Create(workspace,catalog);}}
sealed class EmptyCatalog:IPackageCatalogSource
{public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version,Revision="empty",Packages=[]};}
sealed class CustomRunner:IEditorCommandLineRunner
{public int Run(IReadOnlyList<string> arguments,BPackageCatalog? catalog=null)=>7;}
public sealed class UnrelatedTool:IEditorCommandLineTool
{public static int Constructions;public UnrelatedTool(){Constructions++;}public string Option=>"--fixture";public int Run(IReadOnlyList<string> arguments)=>throw new Exception("unrelated");}
