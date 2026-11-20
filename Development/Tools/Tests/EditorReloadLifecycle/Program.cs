using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;

new Probe(args[0], Path.GetFullPath(args[1])).Run();

sealed class Probe(string mode, string output)
{
    readonly List<string> checks = [];
    void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
    T Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T e) { Check(true, name); return e; } throw new Exception(name); }
    public void Run()
    {
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
        Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", Path.Combine(output,"Packages"));
        DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
        using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
            .AddBEngineEditorCompilation().AddSingleton<IPackageCatalogSource>(new EmptyCatalog()).BuildServiceProvider();
        PlatformRuntime.Initialize(services); using var attachment = EditorServiceScope.Attach(services);
        Check(services.GetService<IEditorNativeWindowFactory>() is null, "The lifecycle consumer needs no window or GPU");
        Check(typeof(AssemblyReloadEvents).Assembly == typeof(ProjectScriptHostSession).Assembly, "Assembly coordination belongs to Core Editor");
        Check(!typeof(AssemblyReloadEvents).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Any(a => a.AssemblyName.Split(',')[0] == GetType().Assembly.GetName().Name), "The consumer has no Core Editor friend access");
        if (mode == "Events") Events(); else if (mode == "Initialization") Initialization(); else throw new ArgumentException(mode);
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.EditorWorkbench.Host" or "BEngine.Graphics" or "BEngine.Application"), "Lifecycle coordination loads no workbench, graphics or framework");
        File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"EDITOR_RELOAD_{mode}_OK {checks.Count}");
    }
    void Events()
    {
        var calls = new List<string>(); Action before = () => calls.Add("before"), after = () => calls.Add("after");
        AssemblyReloadEvents.beforeAssemblyReload += before; AssemblyReloadEvents.afterAssemblyReload += after;
        var previousIsolation = EditorFeatureIsolation.Current;
        try
        {
            using(var initial = AssemblyReloadEvents.BeginInitialization())
            {
                Check(calls.Count==0,"First-load coordination does not emit a reload-before event");
                Throws<InvalidOperationException>(()=>AssemblyReloadEvents.BeginReload(),"First load excludes overlapping reloads");
                initial.Complete(); initial.Complete();
                Check(calls.SequenceEqual(["after"]),"First-load readiness is explicit and emitted once");
            }
            Check(calls.Count==1,"Disposing a completed first load is idempotent");
            calls.Clear(); using(var aborted = AssemblyReloadEvents.BeginInitialization()) { }
            Check(calls.Count==0,"An aborted first load does not announce readiness");
            using(var reload = AssemblyReloadEvents.BeginReload())
            {
                Check(calls.SequenceEqual(["before"]),"Reload-before occurs before replacement work");
                Throws<InvalidOperationException>(()=>AssemblyReloadEvents.BeginInitialization(),"Reload excludes overlapping first loads");
                Task.Run(()=>Throws<InvalidOperationException>(reload.Complete,"A worker cannot finish the editor's transaction")).GetAwaiter().GetResult();
                Task.Run(()=>Throws<InvalidOperationException>(reload.Dispose,"A worker cannot dispose the editor's transaction")).GetAwaiter().GetResult();
                Check(calls.Count==1,"Rejected worker cleanup preserves the owning transaction");
            }
            Check(calls.SequenceEqual(["before","after"]),"Owner disposal pairs the reload notification");
            calls.Clear(); var completed=AssemblyReloadEvents.BeginReload(); completed.Complete(); completed.Dispose(); completed.Complete();
            Check(calls.SequenceEqual(["before","after"]),"Complete and Dispose never notify twice");
            calls.Clear();
            try { using var failed=AssemblyReloadEvents.BeginReload(); throw new IOException("replacement failure"); }
            catch(IOException) { }
            Check(calls.SequenceEqual(["before","after"]),"An early replacement failure still pairs notifications");
            var nested=0; Action reentrant=()=>{Throws<InvalidOperationException>(()=>AssemblyReloadEvents.BeginReload(),"Notification reentrancy cannot start a second transaction");nested++;};
            AssemblyReloadEvents.beforeAssemblyReload += reentrant; AssemblyReloadEvents.afterAssemblyReload += reentrant;
            try { using var reload=AssemblyReloadEvents.BeginReload(); }
            finally { AssemblyReloadEvents.beforeAssemblyReload-=reentrant;AssemblyReloadEvents.afterAssemblyReload-=reentrant; }
            Check(nested==2,"Both before and after callbacks retain transaction ownership");
            calls.Clear(); EditorFeatureIsolation.Current=new FailingIsolation(1);
            Throws<InvalidOperationException>(()=>AssemblyReloadEvents.BeginReload(),"A failing before-notification backend reaches its caller");
            Check(calls.SequenceEqual(["before","after"]),"Failed notification setup still sends its paired after event");
            calls.Clear(); EditorFeatureIsolation.Current=new FailingIsolation(2);
            var both=Throws<AggregateException>(()=>AssemblyReloadEvents.BeginReload(),"Setup and after-notification failures are both preserved");
            Check(both.InnerExceptions.Count==2,"Both lifecycle failures remain inspectable");
            EditorFeatureIsolation.Current=null; calls.Clear();
            var failingFinish=AssemblyReloadEvents.BeginReload(); EditorFeatureIsolation.Current=new FailingIsolation(1);
            Throws<InvalidOperationException>(failingFinish.Dispose,"An after-notification backend failure reaches its caller");
            EditorFeatureIsolation.Current=null; failingFinish.Dispose();
            using(var next=AssemblyReloadEvents.BeginReload()) { }
            Check(calls.SequenceEqual(["before","after","before","after"]),"Failure releases ownership and repeated disposal cannot affect the next transaction");
            calls.Clear();
            Action bad=()=>throw new IOException("handler failure"), exit=()=>throw new EditorCallbackExitException();
            AssemblyReloadEvents.beforeAssemblyReload+=bad; AssemblyReloadEvents.afterAssemblyReload+=exit;
            try { using var reload=AssemblyReloadEvents.BeginReload(); }
            finally { AssemblyReloadEvents.beforeAssemblyReload-=bad;AssemblyReloadEvents.afterAssemblyReload-=exit; }
            Check(calls.SequenceEqual(["before","after"]),"Ordinary callback failures and exit signals retain existing dispatcher containment");
        }
        finally { EditorFeatureIsolation.Current=previousIsolation;AssemblyReloadEvents.beforeAssemblyReload-=before;AssemblyReloadEvents.afterAssemblyReload-=after; }
    }
    void Initialization()
    {
        var fixtures=Path.Combine("E:/Project/_BZP/BEngine/Temp/RL41",Guid.NewGuid().ToString("N")[..8]);
        var root=Path.Combine(fixtures,"Project"); var isolated=Path.Combine(fixtures,"Isolated");
        var log=Path.Combine(output,"Initialization.txt");Environment.SetEnvironmentVariable("RL41_LOG",log);
        Create(root,"owner");Create(isolated,"isolated");
        var workspace=ProjectWorkspace.Open(root);var other=ProjectWorkspace.Open(isolated);
        var weak=RunHost(workspace,other,log);
        for(var i=0;i<25 && weak.Any(w=>w.IsAlive);i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Thread.Sleep(20);}
        Check(weak.All(w=>!w.IsAlive),"Initialized project assemblies and load contexts collect after owner retirement");
        Check(EditorInstanceContext.current is null,"The caller still owns and releases the project lock");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    WeakReference[] RunHost(ProjectWorkspace workspace,ProjectWorkspace other,string log)
    {
        using var packages=BPackageManager.OpenManifest(workspace);
        using var instance=EditorInstanceContext.Create(workspace.RootPath);
        var host=ProjectScriptHostSession.Attach(workspace,packages);
        try
        {
            host.CompileRuntime();host.CompileEditor();
            var initial=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.IsCollectible && a.GetName().Name is "GameScripts" or "GameEditorScripts").ToArray();
            WeakReference[] weak=initial.SelectMany(a=>new[]{new WeakReference(a),new WeakReference(AssemblyLoadContext.GetLoadContext(a))}).ToArray();
            using(var independent=ProjectScriptSession.Open(other))
            {
                host.PrepareEditorExtensions();
                Check(!File.Exists(log),"Preparing reflection does not initialize either project");
                var notifications=0;Action callback=()=>notifications++;AssemblyReloadEvents.afterAssemblyReload+=callback;
                try { host.InitializeEditorExtensions(synchronizeReflection:false); }
                finally { AssemblyReloadEvents.afterAssemblyReload-=callback; }
                Check(notifications==0,"Initializing extensions does not publish the host's lifecycle readiness");
                var first=File.ReadAllLines(log);
                Check(first.Contains("owner-class") && first.Contains("owner-init"),"The owning project's class and method initializers execute");
                Check(first.SequenceEqual(["owner-class","owner-init","owner-reload-2","owner-reload-9"]),"Reload callbacks retain their declared order after initialization");
                Check(!first.Any(s=>s.StartsWith("isolated")),"An unrelated loaded collectible tool graph is never initialized");
                host.InitializeEditorExtensions();var again=File.ReadAllLines(log);
                Check(again.Count(s=>s=="owner-class")==1 && again.Count(s=>s=="owner-init")==1,"Repeated initialization does not rerun class or load-method initializers");
                Check(again.Count(s=>s=="owner-reload-2")==2 && again.Count(s=>s=="owner-reload-9")==2,"Each explicit scripts-reloaded pass still notifies reload methods");
                Task.Run(()=>Throws<InvalidOperationException>(host.PrepareEditorExtensions,"A worker cannot prepare editor extensions")).GetAwaiter().GetResult();
                Task.Run(()=>Throws<InvalidOperationException>(()=>host.InitializeEditorExtensions(),"A worker cannot initialize editor extensions")).GetAwaiter().GetResult();
                host.Release();host.InitializeEditorExtensions();
                Check(File.ReadAllLines(log).Length==again.Length,"Releasing the owned graph prevents its callbacks from running again");
                host.CompileRuntime();host.CompileEditor();host.InitializeEditorExtensions();
                Check(File.ReadAllLines(log).Count(s=>s=="owner-init")==2,"A recompiled graph initializes its new assembly identity once");
                Check(!File.ReadAllLines(log).Any(s=>s.StartsWith("isolated")),"Reload and retirement never initialize the other tool's assemblies");
            }
            host.Dispose();
            Throws<ObjectDisposedException>(host.PrepareEditorExtensions,"Disposed hosts cannot prepare caches");
            Throws<ObjectDisposedException>(()=>host.InitializeEditorExtensions(),"Disposed hosts cannot invoke initialization");
            return weak;
        }
        finally { host.Dispose(); }
    }
    static void Create(string root,string tag)
    {
        Directory.CreateDirectory(Path.Combine(root,"Assets/Editor"));Directory.CreateDirectory(Path.Combine(root,"Packages"));
        File.WriteAllText(Path.Combine(root,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: ReloadLifecycle\n");
        File.WriteAllText(Path.Combine(root,"Packages/manifest.yaml"),"packages: []\n");
        File.WriteAllText(Path.Combine(root,"Assets/Runtime.cs"),"public static class RuntimeMarker { public static int Value => 1; }");
        File.WriteAllText(Path.Combine(root,"Assets/Editor/Tool.cs"),$$"""
            using System; using System.IO; using BEngine.Editor;
            namespace RL41;
            [InitializeOnLoad] public static class Eager { static Eager()=>Log.Write("{{tag}}-class"); }
            public static class Log {
              public static void Write(string text)=>File.AppendAllText(Environment.GetEnvironmentVariable("RL41_LOG")!,text+"\n");
              [InitializeOnLoadMethod] static void Init()=>Write("{{tag}}-init");
              [DidReloadScripts(9)] static void Later()=>Write("{{tag}}-reload-9");
              [DidReloadScripts(2)] static void Earlier()=>Write("{{tag}}-reload-2");
            }
            """);
    }
}
sealed class EmptyCatalog : IPackageCatalogSource
{
    public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version,Revision="empty",Packages=[]};
}
sealed class FailingIsolation(int remaining) : IEditorFeatureIsolation
{
    public int Begin()=>0;
    public void Restore(int token,bool succeeded){if(remaining-->0)throw new InvalidOperationException("fixture presentation restore failure");}
}
