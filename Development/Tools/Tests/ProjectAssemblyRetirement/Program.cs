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

new Probe(args[0],Path.GetFullPath(args[1])).Run();

sealed class Probe(string mode,string output)
{
    readonly List<string> checks=[];
    string root="";
    ProjectWorkspace workspace=null!;
    static readonly PackageEditorTemplateContext TemplateContext=new("Test","Fixture","Window");
    void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
    public void Run()
    {
        Directory.CreateDirectory(output);
        root=Path.Combine("E:/Project/_BZP/BEngine/Temp/PT34",Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
        Environment.SetEnvironmentVariable("PT34_CALLBACK",Path.Combine(output,"Callbacks.txt"));
        DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
        using var host=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
            .AddSingleton<IPackageCatalogSource,EmptyCatalog>().BuildServiceProvider();
        PlatformRuntime.Initialize(host);using var attachment=EditorServiceScope.Attach(host);
        Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a=>a.AssemblyName.Split(',')[0]==typeof(Probe).Assembly.GetName().Name),"Consumer has no private access grant");
        CreateProject();
        switch(mode){case "Success":Success();break;case "Isolation":Isolation();break;case "Failure":Failure();break;case "Collection":Collection();break;default:throw new ArgumentException(mode);}
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Application" or "BEngine.Graphics"),"Retirement needs no concrete UI, graphics or game-framework package");
        File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Names=checks,Fixture=root},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PROJECT_RETIREMENT_{mode.ToUpperInvariant()}_OK {checks.Count}");
    }
    void Success()
    {
        var session=Open("A");var assembly=Editor(session);var type=Tool(session);
        Check(Template()=="A","Project template provider is active");
        Check(Providers().Contains("Project/PT34/A"),"Project settings provider is discoverable");
        Check(RuntimeBuildResources.GetDirectories().Any(x=>x.RuntimeAssembly=="Fixture.A"),"Project startup resource contribution is active");
        Initialize(assembly);Check((int)type.GetField("Initializations")!.GetValue(null)! == 1,"Editor initialization is invoked before retirement");
        _=TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>();
        _=TypeCache.GetTypesDerivedFrom(typeof(IScriptSourceProcessor));
        session.Dispose();session.Dispose();
        Check(Template()!="A","Retired template provider is removed");
        Check(!Providers().Contains("Project/PT34/A"),"Retired settings provider is removed");
        Check(!RuntimeBuildResources.GetDirectories().Any(x=>x.RuntimeAssembly=="Fixture.A"),"Retired startup resources are removed");
        Check(!TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>().Any(m=>m.DeclaringType?.Assembly==assembly),"Cached attributed methods exclude retired code");
        Check(!TypeCache.GetTypesDerivedFrom(typeof(IScriptSourceProcessor)).Any(t=>t.Assembly==assembly),"Cached derived types exclude retired code");
        EditorExtensionLifecycle.SynchronizeReflection();Check(!Callbacks().Contains("A"),"Reflection synchronization does not run retired warmups");
        using var again=Open("B");Initialize(Editor(again));
        Check(assembly.ManifestModule.ModuleVersionId==Editor(again).ManifestModule.ModuleVersionId,"Reload fixture reuses the identical compiled module identity");
        Check((int)Tool(again).GetField("Initializations")!.GetValue(null)! == 1,"Reloading the same compiled MVID runs the new owner's initialization");
        Check(Template()=="B"&&Providers().Contains("Project/PT34/B"),"Reloaded owner can register its own capabilities");
    }
    void Isolation()
    {
        using var a=Open("A");
        // The normal type index selects one owner for a full-name collision. Use
        // different tool types in two revisions with the same assembly name so
        // both providers are discoverable and exact assembly retirement is tested.
        var editorSource=Path.Combine(root,"Assets/Editor/EditorProbe.cs");
        File.WriteAllText(editorSource,File.ReadAllText(editorSource).Replace("EditorProbe","EditorProbeB",StringComparison.Ordinal));
        using var b=Open("B");var aa=Editor(a);var ba=Editor(b);
        Check(aa.GetName().Name==ba.GetName().Name&&aa!=ba,"Fixture has two collectible assemblies with the same simple name");
        Check(Providers().Contains("Project/PT34/A")&&Providers().Contains("Project/PT34/B"),"Both owners' settings are initially available");
        var retiredCalls=0;var survivingCalls=0;
        Action changed=()=>{
            var methods=TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>();
            // The session retires Runtime and Editor assemblies separately. An
            // earlier Runtime notification does not yet retire the Editor owner.
            if(!methods.Any(m=>m.DeclaringType?.Assembly==aa)&&Providers().Contains("Project/PT34/A"))retiredCalls++;
            if(methods.Any(m=>m.DeclaringType?.Assembly==ba))survivingCalls++;
        };
        SettingsProviderRegistry.changed+=changed;
        try{a.Dispose();}finally{SettingsProviderRegistry.changed-=changed;}
        Check(retiredCalls==0,"Retirement notification cannot rediscover the retired settings provider");
        Check(survivingCalls>0,"Retirement notification can still discover the surviving owner");
        Check(Template()=="B","Retiring one owner keeps the other template provider");
        Check(!Providers().Contains("Project/PT34/A")&&Providers().Contains("Project/PT34/B"),"Exact assembly ownership preserves providers from the same-name surviving assembly");
        EditorExtensionLifecycle.SynchronizeReflection();Check(Callbacks().Contains("B")&&!Callbacks().Contains("A"),"Only surviving warmups run");
        Check(RuntimeBuildResources.GetDirectories().Any(x=>x.RuntimeAssembly=="Fixture.B")&&!RuntimeBuildResources.GetDirectories().Any(x=>x.RuntimeAssembly=="Fixture.A"),"Resource retirement affects only its owner");
        b.Dispose();Check(Template()!="A"&&Template()!="B"&&Providers().All(x=>!x.StartsWith("Project/PT34/")),"All retired owners leave a clean registry");
    }
    void Failure()
    {
        using var survivor=Open("survivor");
        ProjectScriptSession? failed=null;var failureObserved=false;
        Environment.SetEnvironmentVariable("PT34_FAIL_MODULE","1");
        try{failed=ProjectScriptSession.Open(workspace);Tool(failed).GetMethod("Install")!.Invoke(null,["never"]);}
        catch(TargetInvocationException e){failureObserved=e.ToString().Contains("fixture module failure");}
        finally{Environment.SetEnvironmentVariable("PT34_FAIL_MODULE",null);failed?.Dispose();}
        Check(failureObserved,"Real module initialization failure reaches its caller");
        Check(Template()=="survivor","Failed module's partially registered template is retired");
        Check(!RuntimeBuildResources.GetDirectories().Any(x=>x.RuntimeAssembly=="Fixture.failed"),"Failed module's resources are retired");
        Check(Providers().Contains("Project/PT34/survivor"),"Failure does not discard surviving settings");
        RuntimeAssemblyCleanup.Register(owner=>{if(owner.GetName().Name=="GameEditorScripts"&&Environment.GetEnvironmentVariable("PT34_FAIL_CLEANUP")=="1")throw new InvalidOperationException("fixture cleanup failure");});
        var bad=Open("cleanup");Environment.SetEnvironmentVariable("PT34_FAIL_CLEANUP","1");
        var cleanupObserved=false;try{bad.Dispose();}catch(AggregateException e){cleanupObserved=e.ToString().Contains("fixture cleanup failure");}finally{Environment.SetEnvironmentVariable("PT34_FAIL_CLEANUP",null);}
        Check(cleanupObserved,"Cleanup failures are reported");
        bad.Dispose();Check(Template()=="survivor"&&!Providers().Contains("Project/PT34/cleanup"),"Other cleanup continues after one extension throws and disposal remains idempotent");
        EditorExtensionLifecycle.SynchronizeReflection();Check(!Callbacks().Contains("cleanup")&&!Callbacks().Contains("failed")&&Callbacks().Contains("survivor"),"Failure cleanup removes retired callbacks and preserves live ones");
        File.WriteAllText(Path.Combine(root,"Assets/Editor/EditorProbe.cs"),"invalid C#");
        var compilationObserved=false;try{ProjectScriptSession.Open(workspace);}catch(InvalidOperationException){compilationObserved=true;}
        Check(compilationObserved&&Template()=="survivor","Compiler failure preserves an already active owner");
    }
    void Collection()
    {
        var weak=CreateAndRetire();
        for(var i=0;i<12&&weak.Any(w=>w.IsAlive);i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Thread.Sleep(25);}
        Check(weak.All(w=>!w.IsAlive),"Retired assembly, load context and settings delegates are collectable without a later cache query");
        Check(!Callbacks().Contains("collect"),"Collection did not invoke retired extension code");
        EditorExtensionLifecycle.SynchronizeReflection();Check(!Callbacks().Contains("collect"),"Later reflection warmup cannot resurrect the retired owner");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    WeakReference[] CreateAndRetire()
    {
        var session=Open("collect");var assembly=Editor(session);
        var provider=SettingsProviderRegistry.GetProviders(SettingsScope.Project).Single(p=>p.settingsPath=="Project/PT34/collect");
        _=TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>();
        _=TypeCache.GetTypesDerivedFrom(typeof(IScriptSourceProcessor));
        WeakReference[] weak=[new(assembly),new(AssemblyLoadContext.GetLoadContext(assembly)),new(provider)];
        session.Dispose();return weak;
    }
    ProjectScriptSession Open(string marker){var s=ProjectScriptSession.Open(workspace);try{Tool(s).GetMethod("Install")!.Invoke(null,[marker]);RuntimeTypeCache.RegisterAssemblies(s.Assemblies);SettingsProviderRegistry.Invalidate();return s;}catch{s.Dispose();throw;}}
    static Type Tool(ProjectScriptSession s)=>s.Assemblies.SelectMany(a=>a.GetTypes()).Single(t=>t.Name.StartsWith("EditorProbe",StringComparison.Ordinal));
    static Assembly Editor(ProjectScriptSession s)=>Tool(s).Assembly;
    static string Template()=>PackageEditorTemplates.Create(TemplateContext).Source;
    static string[] Providers()=>SettingsProviderRegistry.GetProviders(SettingsScope.Project).Select(p=>p.settingsPath).ToArray();
    string[] Callbacks()=>File.Exists(Path.Combine(output,"Callbacks.txt"))?File.ReadAllLines(Path.Combine(output,"Callbacks.txt")):[];
    // Exercise the existing owner mechanism; this probe does not grant private access
    // to a package or widen the editor initialization API.
    static void Initialize(Assembly assembly)=>typeof(EditorServices).Assembly.GetType("BEngine.Editor.EditorInitialization")!
        .GetMethod("Run",BindingFlags.Static|BindingFlags.NonPublic,null,[typeof(IEnumerable<Assembly>),typeof(bool)],null)!.Invoke(null,[new[]{assembly},false]);
    void CreateProject()
    {
        Directory.CreateDirectory(Path.Combine(root,"Assets/Editor"));Directory.CreateDirectory(Path.Combine(root,"Packages"));
        File.WriteAllText(Path.Combine(root,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: RetirementProbe\n");
        File.WriteAllText(Path.Combine(root,"Packages/manifest.yaml"),"packages: []\n");
        File.WriteAllText(Path.Combine(root,"Assets/RuntimeProbe.cs"),"public sealed class RuntimeProbe {}");
        File.WriteAllText(Path.Combine(root,"Assets/Editor/EditorProbe.cs"),"""
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.ProjectSystem;
public static class EditorProbe {
 public static string Marker="uninstalled";
 public static int Initializations;
 [ModuleInitializer] public static void Boot(){if(Environment.GetEnvironmentVariable("PT34_FAIL_MODULE")=="1"){Install("failed");throw new InvalidOperationException("fixture module failure");}}
 public static void Install(string marker) {
  Marker=marker;
  PackageEditorTemplates.Register(_=>new(Marker,[],[]));
  TypeCache.RegisterWarmup("PT34/"+Marker,()=>File.AppendAllText(Environment.GetEnvironmentVariable("PT34_CALLBACK")!,Marker+"\n"));
  RuntimeBuildResources.Register("Fixture."+Marker,"Init");
  ScriptSourceProcessors.Register(new Processor());
 }
 [InitializeOnLoadMethod] static void Initialize()=>Initializations++;
 [SettingsProvider] public static SettingsProvider Settings()=>new("Project/PT34/"+Marker,SettingsScope.Project){guiHandler=Draw};
 static void Draw(string search) {}
}
public sealed class Processor:IScriptSourceProcessor {
 public string Revision=>"fixture";
 public string Transform(string text,string path,IReadOnlyList<string> symbols)=>text;
}
""");
        workspace=ProjectWorkspace.Open(root);
    }
}
sealed class EmptyCatalog:IPackageCatalogSource{public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version};}
