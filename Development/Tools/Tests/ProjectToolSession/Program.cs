using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
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
using App = BEngine.Application;

new Probe(args[0],Path.GetFullPath(args[1])).Run();

sealed class Probe(string mode,string output)
{
    readonly List<string> checks=[];
    readonly Catalog catalog=new();
    string root="",events="",data="",temp="",tmp="";
    ProjectWorkspace workspace=null!;
    void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
    void Throws<T>(Action action,string name) where T:Exception {try{action();}catch(T){Check(true,name);return;}throw new InvalidOperationException(name);}
    public void Run()
    {
        Directory.CreateDirectory(output);
        root=Path.Combine("E:/Project/_BZP/BEngine/Temp/PT35",Guid.NewGuid().ToString("N")[..8]);
        events=Path.Combine(output,"Events.txt");
        File.WriteAllText(events,"");
        Environment.SetEnvironmentVariable("PT35_EVENTS",events);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
        Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH",Path.Combine(root,"Hub/Packages"));
        DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
        using var host=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
            .AddSingleton<IPackageCatalogSource>(catalog).BuildServiceProvider(new ServiceProviderOptions{ValidateOnBuild=true,ValidateScopes=true});
        PlatformRuntime.Initialize(host);using var attachment=EditorServiceScope.Attach(host);
        Check(host.GetService<IEditorNativeWindowFactory>() is null,"Validated tooling services require no native window backend");
        Check(!typeof(EditorServices).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a=>a.AssemblyName.Split(',')[0]==typeof(Probe).Assembly.GetName().Name),"Consumer receives no private access grant");
        CreateProject();data=App.dataPath;temp=Environment.GetEnvironmentVariable("TEMP")!;tmp=Environment.GetEnvironmentVariable("TMP")!;
        RuntimePackageState.SetEnabled("prior.disabled",false);
        RuntimePackageState.SetEnabled("com.fixture.tools",false);
        switch(mode){case "Determinism":Determinism();break;case "Lifecycle":Lifecycle();break;case "Isolation":Isolation();break;case "Failure":Failure();break;case "Importer":Importer();break;case "Collection":Collection();break;default:throw new ArgumentException(mode);}
        Restored();
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.EditorWorkbench.Editor" or "BEngine.Application"),"Generic project tooling does not load the default workbench or game framework");
        File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Names=checks,Fixture=root},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PROJECT_TOOL_{mode.ToUpperInvariant()}_OK {checks.Count}");
    }
    string[] Events()=>File.Exists(events)?File.ReadAllLines(events):[];
    static Assembly ProjectEditor()=>RuntimeTypeCache.GetAllTypes().Single(t=>t.FullName=="PT35.ProjectEditor").Assembly;
    void Restored()
    {
        Check(EditorInstanceContext.current is null,"Exclusive project instance is released");
        Check(App.dataPath==data,"Previous resource path is restored");
        Check(Environment.GetEnvironmentVariable("TEMP")==temp&&Environment.GetEnvironmentVariable("TMP")==tmp,"Previous process temporary paths are restored");
        Check(!RuntimePackageState.IsEnabled("prior.disabled")&&!RuntimePackageState.IsEnabled("com.fixture.tools")&&RuntimePackageState.IsEnabled("only.inside.session"),"Package state restores old values and removes new overrides");
        Check(!TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>().Any(m=>m.DeclaringType?.FullName=="PT35.ProjectEditor"),"Retired project editor callbacks leave no reflected registration");
    }
    void Determinism()
    {
        // Exercise the actual private Player compilation configuration without
        // granting this consumer friend access or exposing compiler internals.
        var editor=typeof(ProjectToolSession).Assembly;
        var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        object Read(object value,string name)=>value.GetType().GetProperty(name,flags)!.GetValue(value)!;
        var configuration=Activator.CreateInstance(editor.GetType("BEngine.ProjectSystem.ScriptBuildConfiguration")!,
            ["net10.0",RuntimePlatform.WebGLPlayer,Array.Empty<string>(),false,"Release",false,false,false])!;
        var rows=new List<object>();var packages=new List<byte[]>();var projects=new List<byte[]>();
        for(var i=0;i<3;i++)
        {
            if(i==2)
            {
                File.AppendAllText(Path.Combine(root,"Hub/Packages/Fixture/Fixture-1.0.0/Fixture.Tools/Source.cs")," public static class PackageRevision { public const int Value=36; }");
                File.AppendAllText(Path.Combine(root,"Assets/Runtime.cs")," public static class ProjectRevision { public const int Value=36; }");
            }
            using var session=ProjectToolSession.Open(workspace);
            using var manifest=BPackageManager.OpenManifest(workspace);
            var compile=editor.GetType("BEngine.ProjectSystem.PackageSourceCompiler")!.GetMethods(flags).Single(m=>m.Name=="CompileEnabled"&&m.GetParameters().Length==8);
            var result=compile.Invoke(null,[workspace,manifest.definitions,ProcessUtility.Current,null,false,configuration,CancellationToken.None,false])!;
            var references=new Dictionary<string,string>((IReadOnlyDictionary<string,string>)Read(result,"AssemblyPaths"),StringComparer.OrdinalIgnoreCase);
            var package=File.ReadAllBytes(references["Fixture.Tools"]);packages.Add(package);
            references["BEngine.Core"]=typeof(BAsset).Assembly.Location;
            var database=new ProjectAssemblyDatabase(workspace);
            var graph=typeof(ProjectAssemblyDatabase).GetMethods(flags).Single(m=>m.Name=="BuildGraph"&&m.GetParameters().Length==7).Invoke(database,
                [RuntimePlatform.WebGLPlayer,new HashSet<string>(),references.Keys.ToHashSet(),false,null,null,CancellationToken.None])!;
            var nodes=Read(graph,"RuntimeAssemblies");
            var build=editor.GetType("BEngine.ProjectSystem.ProjectScriptCompiler")!.GetMethod("CompileGraph",flags)!.Invoke(null,
                [workspace,nodes,references,new Dictionary<string,string>(),configuration,false,false,false,CancellationToken.None,ProcessUtility.Current])!;
            var artifacts=((System.Collections.IEnumerable)Read(build,"CompiledAssemblies")).Cast<object>().ToArray();
            var projectPath=(string)Read(artifacts.Single(),"AssemblyPath");
            var project=File.ReadAllBytes(projectPath);projects.Add(project);
            if(i==0)
            {
                using var symbols=System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(Path.ChangeExtension(projectPath,".pdb")));
                var reader=symbols.GetMetadataReader();
                Check(reader.Documents.Select(h=>reader.GetString(reader.GetDocument(h).Name)).Any(p=>Path.GetFullPath(p)==Path.GetFullPath(Path.Combine(root,"Assets/Runtime.cs"))),"Project debug symbols retain original source paths");
            }
            File.WriteAllBytes(Path.Combine(output,$"Package-{i}.dll"),package);File.WriteAllBytes(Path.Combine(output,$"Project-{i}.dll"),project);
            rows.Add(new{Instance=EditorInstanceContext.current!.dataPath,PackageSHA256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)),ProjectSHA256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(project))});
        }
        File.WriteAllText(Path.Combine(output,"Images.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Check((string)Read(rows[0],"Instance")!=(string)Read(rows[1],"Instance"),"Identical images come from distinct exclusive instances");
        Check(!packages[0].SequenceEqual(packages[2]),"Changed package source invalidates the release image");
        Check(!projects[0].SequenceEqual(projects[2]),"Changed project source invalidates the release image");
        Check(packages[0].SequenceEqual(packages[1]),"Package release images are identical across exclusive instance directories");
        Check(projects[0].SequenceEqual(projects[1]),"Project release images are identical across exclusive instance directories");
    }

    void Lifecycle()
    {
        var starts=new List<string>();var finishes=new List<string>();
        Action<string> started=path=>starts.Add(path);Action<string,CompilerMessage[]> finished=(path,messages)=>{Check(messages.Length==0,"Successful compilation reports no diagnostics");finishes.Add(path);};
        CompilationPipeline.assemblyCompilationStarted+=started;CompilationPipeline.assemblyCompilationFinished+=finished;
        try
        {
            var session=ProjectToolSession.Open(workspace);var instance=EditorInstanceContext.current!;
            Check(instance.projectRootPath==workspace.RootPath&&App.dataPath==workspace.AssetsPath,"Open acquires the project lock and activates its resource path");
            Throws<IOException>(()=>{using var collision=File.Open(Path.Combine(root,"Library/.bengine-editor.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);},"The project lock rejects a second file handle while active");
            Check(RuntimePackageState.IsEnabled("com.fixture.tools"),"Enabled package state is active during the session");
            RuntimePackageState.SetEnabled("only.inside.session",false);
            Check(Events().Contains("package-module")&&!Events().Contains("project-editor-module"),"Package module activation precedes caller policy and project compilation");
            // A framework can generate configuration/scripts at exactly this point.
            File.WriteAllText(Path.Combine(root,"Assets/GeneratedPolicy.cs"),"namespace PT35; public static class Policy { public const int Ready=35; }");
            File.AppendAllText(events,"policy\n");
            session.InitializeScripts();
            Check(Events().Contains("project-initialize:35"),"Project editor initialization sees caller-generated runtime code");
            var lines=Events();Check(Array.IndexOf(lines,"package-module")<Array.IndexOf(lines,"policy")&&Array.IndexOf(lines,"policy")<Array.IndexOf(lines,"project-runtime-module")&&Array.IndexOf(lines,"project-runtime-module")<Array.IndexOf(lines,"project-editor-module"),"Package activation, policy, project Runtime and project Editor keep their order");
            Check(starts.Count==2&&starts.Order().SequenceEqual(finishes.Order()),"Actual compiled Runtime and Editor have matched compilation events");
            Check(Directory.GetFiles(workspace.ScriptAssembliesPath,"GameScripts.dll",SearchOption.AllDirectories).Any()&&Directory.GetFiles(instance.scriptAssembliesPath,"GameEditorScripts.dll",SearchOption.AllDirectories).Any(),"Shared and instance compiler outputs are published for downstream tools");
            Check(SettingsProviderRegistry.GetProviders(SettingsScope.Project).Any(p=>p.settingsPath=="Project/PT35"),"Project settings callbacks are active while the session is alive");
            Throws<InvalidOperationException>(()=>session.InitializeScripts(),"Initializing the same session twice is rejected without releasing it");
            session.Dispose();session.Dispose();
            Check(!Directory.Exists(instance.tempPath)&&File.Exists(Path.Combine(instance.dataPath,"Instance.yaml")),"Disposal removes disposable temporary files and retains instance diagnostics");
            Restored();
            using var again=ProjectToolSession.Open(workspace);again.InitializeScripts();
            Check(Events().Count(x=>x=="project-initialize:35")==2,"A new session initializes a fresh owner even with the same compiled module");
        }
        finally{CompilationPipeline.assemblyCompilationStarted-=started;CompilationPipeline.assemblyCompilationFinished-=finished;}
    }
    void Isolation()
    {
        using(var editor=EditorInstanceContext.Create(root))
        {
            var before=App.dataPath;Throws<InvalidOperationException>(()=>ProjectToolSession.Open(workspace),"An existing editor prevents exclusive tool activation");
            Check(ReferenceEquals(EditorInstanceContext.current,editor)&&App.dataPath==before,"Rejected activation preserves the existing editor");
        }
        using var isolated=ProjectScriptSession.Open(workspace);
        Check(!RuntimePackageState.IsEnabled("com.fixture.tools"),"Manifest-only resolution and isolated compilation preserve active package state");
        var isolatedAssembly=isolated.Assemblies.Single(a=>a.GetName().Name=="GameEditorScripts");
        using var session=ProjectToolSession.Open(workspace);
        Throws<InvalidOperationException>(()=>ProjectToolSession.Open(workspace),"Nested exclusive sessions are rejected");
        File.WriteAllText(Path.Combine(root,"Assets/GeneratedPolicy.cs"),"namespace PT35; public static class Policy { public const int Ready=35; }");
        session.InitializeScripts();
        Check((int)isolatedAssembly.GetType("PT35.ProjectEditor")!.GetField("Initializations")!.GetValue(null)! == 0,"Unrelated isolated project assemblies are not initialized");
        session.Dispose();
        Check(isolated.Assemblies.Contains(isolatedAssembly)&&isolatedAssembly.GetType("PT35.ProjectEditor") is not null,"Exclusive disposal preserves the isolated session's code");
        using var reopened=EditorInstanceContext.Create(root);
        Check(reopened.projectRootPath==workspace.RootPath,"The same project lock can be acquired after tool disposal");
    }
    void Failure()
    {
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();Throws<OperationCanceledException>(()=>ProjectToolSession.Open(workspace,canceled.Token),"Pre-canceled activation is rejected");
            var session=ProjectToolSession.Open(workspace);Throws<OperationCanceledException>(()=>session.InitializeScripts(canceled.Token),"Canceled compilation retires the active session");session.Dispose();Restored();
        }
        File.WriteAllText(Path.Combine(root,"Assets/GeneratedPolicy.cs"),"invalid C#");
        var failed=ProjectToolSession.Open(workspace);Throws<InvalidOperationException>(()=>failed.InitializeScripts(),"Actual Runtime compiler failure reaches its caller");failed.Dispose();Restored();
        File.WriteAllText(Path.Combine(root,"Assets/GeneratedPolicy.cs"),"namespace PT35; public static class Policy { public const int Ready=35; }");
        Environment.SetEnvironmentVariable("PT35_FAIL_MODULE","1");
        try{var module=ProjectToolSession.Open(workspace);Throws<TypeInitializationException>(()=>module.InitializeScripts(),"Actual project module failure reaches its caller");module.Dispose();}
        finally{Environment.SetEnvironmentVariable("PT35_FAIL_MODULE",null);}
        Restored();
        RuntimeAssemblyCleanup.Register(owner=>{if(owner.GetName().Name=="Fixture.Tools.Editor"&&Environment.GetEnvironmentVariable("PT35_FAIL_CLEANUP")=="1")throw new InvalidOperationException("fixture package cleanup failure");});
        Environment.SetEnvironmentVariable("PT35_FAIL_PACKAGE_MODULE","1");Environment.SetEnvironmentVariable("PT35_FAIL_CLEANUP","1");
        try{Throws<AggregateException>(()=>ProjectToolSession.Open(workspace),"Package module and rollback failures are both reported without retaining the project lock");}
        finally{Environment.SetEnvironmentVariable("PT35_FAIL_PACKAGE_MODULE",null);Environment.SetEnvironmentVariable("PT35_FAIL_CLEANUP",null);}
        Restored();
        var bad=ProjectToolSession.Open(workspace);bad.InitializeScripts();Environment.SetEnvironmentVariable("PT35_FAIL_CLEANUP","1");
        try{Throws<AggregateException>(bad.Dispose,"A package cleanup failure is reported after retiring the remaining owners");}
        finally{Environment.SetEnvironmentVariable("PT35_FAIL_CLEANUP",null);bad.Dispose();}
        Restored();Check(!TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>().Any(m=>m.DeclaringType?.FullName=="PT35.PackageEditor"),"A throwing package cleanup cannot preserve its editor callbacks");
        using var retry=ProjectToolSession.Open(workspace);retry.InitializeScripts();Check(Events().Contains("project-initialize:35"),"A project can reopen after compilation, module and cleanup failures");
    }
    void Importer()
    {
        using var session=ProjectToolSession.Open(workspace);
        File.WriteAllText(Path.Combine(root,"Assets/GeneratedPolicy.cs"),"namespace PT35; public static class Policy { public const int Ready=35; }");
        session.InitializeScripts();
        ToolingProgress.Report("Headless import","No native window",0.5f);
        Check(EditorUtility.progressInfo.Progress==0.5f,"Tool progress remains available without a native presentation backend");
        ToolingProgress.Clear();
        Check(!EditorUtility.progressInfo.IsVisible,"Headless progress clears without requiring a native window");
        var asset=Path.Combine(root,"Assets/Sample.pt35");File.WriteAllText(asset,"source payload");
        var database=new BEngine.ProjectSystem.Editor.AssetDatabase(workspace);database.Refresh();
        Check(Events().Contains("real-import:source payload"),"A real package AssetImporter executes during an activated headless tool session");
        Check(File.Exists(asset+".meta")&&database.assets.Any(a=>a.AssetPath.EndsWith("Sample.pt35")),"Actual asset import publishes metadata and database records");
        Check(AssetTypeRegistry.Resolve(asset)=="PT35Asset","Custom importer registration remains active for downstream build work");
        session.Dispose();Check(AssetTypeRegistry.Resolve(asset)!="PT35Asset","Package importer registration is retired with its session");
    }
    void Collection()
    {
        var weak=CreateAndRetire();
        for(var i=0;i<16&&weak.Any(w=>w.IsAlive);i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Thread.Sleep(25);}
        Check(weak.All(w=>!w.IsAlive),"Project and package assemblies and load contexts collect after an exclusive session");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] WeakReference[] CreateAndRetire()
    {
        var session=ProjectToolSession.Open(workspace);
        File.WriteAllText(Path.Combine(root,"Assets/GeneratedPolicy.cs"),"namespace PT35; public static class Policy { public const int Ready=35; }");
        session.InitializeScripts();var project=ProjectEditor();var package=RuntimeTypeCache.GetAllTypes().Single(t=>t.FullName=="PT35.PackageEditor").Assembly;
        WeakReference[] result=[new(project),new(AssemblyLoadContext.GetLoadContext(project)),new(package),new(AssemblyLoadContext.GetLoadContext(package))];session.Dispose();return result;
    }
    void CreateProject()
    {
        Directory.CreateDirectory(Path.Combine(root,"Assets/Editor"));Directory.CreateDirectory(Path.Combine(root,"Packages"));
        File.WriteAllText(Path.Combine(root,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: ToolFixture\n");
        File.WriteAllText(Path.Combine(root,"Packages/manifest.yaml"),"packages:\n- id: com.fixture.tools\n  version: 1.0.0\n  enabled: true\n");
        // Isolation opens the original project without needing caller-generated policy.
        File.WriteAllText(Path.Combine(root,"Assets/GeneratedPolicy.cs"),"namespace PT35; public static class Policy { public const int Ready=35; }");
        File.WriteAllText(Path.Combine(root,"Assets/Runtime.cs"),"""
using System;using System.IO;using System.Runtime.CompilerServices;
namespace PT35; public static class ProjectRuntime { [ModuleInitializer] public static void Boot(){File.AppendAllText(Environment.GetEnvironmentVariable("PT35_EVENTS")!,"project-runtime-module\n");} }
""");
        File.WriteAllText(Path.Combine(root,"Assets/Editor/Tool.cs"),"""
using System;using System.IO;using System.Runtime.CompilerServices;using BEngine.Editor;
namespace PT35; public static class ProjectEditor {
 public static int Initializations;
 [ModuleInitializer] public static void Boot(){File.AppendAllText(Environment.GetEnvironmentVariable("PT35_EVENTS")!,"project-editor-module\n");if(Environment.GetEnvironmentVariable("PT35_FAIL_MODULE")=="1")throw new InvalidOperationException("fixture project module failure");}
 [InitializeOnLoadMethod] static void Initialize(){Initializations++;File.AppendAllText(Environment.GetEnvironmentVariable("PT35_EVENTS")!,"project-initialize:"+Policy.Ready+"\n");}
 [SettingsProvider] public static SettingsProvider Settings()=>new("Project/PT35",SettingsScope.Project){guiHandler=_=>{}};
}
""");
        var folder=Path.Combine(root,"Hub/Packages/Fixture/Fixture-1.0.0");Directory.CreateDirectory(folder);
        var document=new PackageDefinitionDocument{Id="com.fixture.tools",DisplayName="Fixture Tools",MinimumEngineVersion=EngineVersion.Current,Runtime=new(){Assembly="Fixture.Tools",RootNamespace="PT35"},Editor=new(){Assembly="Fixture.Tools.Editor",RootNamespace="PT35",HostReferences=mode=="Importer"?["BEngine.Core.Editor"]:[]}};
        document.Save(Path.Combine(folder,"package.yaml"));catalog.Entries.Add(new(){Path=Path.Combine(folder,"package.yaml"),Document=document});
        Source("Fixture.Tools",false,"namespace PT35; public sealed class FixtureAsset:BEngine.BAsset {}");
        var import=mode=="Importer"?"AssetTypeRegistry.Register<FixtureAsset>(\".pt35\",\"PT35Asset\",importerType:typeof(Importer));":"";
        Source("Fixture.Tools.Editor",true,"""
using System;using System.IO;using System.Runtime.CompilerServices;using BEngine.Editor;
namespace PT35; public static class PackageEditor {
 [ModuleInitializer] public static void Boot(){File.AppendAllText(Environment.GetEnvironmentVariable("PT35_EVENTS")!,"package-module\n");IMPORT if(Environment.GetEnvironmentVariable("PT35_FAIL_PACKAGE_MODULE")=="1")throw new InvalidOperationException("fixture package module failure");}
 [InitializeOnLoadMethod] static void Init()=>File.AppendAllText(Environment.GetEnvironmentVariable("PT35_EVENTS")!,"package-initialize\n");
 [SettingsProvider] public static SettingsProvider Settings()=>new("Project/PT35/Package",SettingsScope.Project);
}
""".Replace("IMPORT",import,StringComparison.Ordinal)+(mode=="Importer"?"public sealed class Importer:AssetImporter {public override void Import(AssetImportContext context){File.AppendAllText(Environment.GetEnvironmentVariable(\"PT35_EVENTS\")!,\"real-import:\"+File.ReadAllText(context.sourcePath)+\"\\n\");context.CopySourceToArtifact();}}":""));
        void Source(string name,bool editor,string source)
        {
            var path=Path.Combine(folder,name);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"Source.cs"),source);
            File.WriteAllText(Path.Combine(path,name+".asmdef.yaml"),YamlUtility.Serialize(new AssemblyDefinitionDocument{Name=name,RootNamespace="PT35",EditorOnly=editor,References=editor?(mode=="Importer"?["Fixture.Tools","BEngine.Core.Editor"]:["Fixture.Tools"]):[]}));
        }
        workspace=ProjectWorkspace.Open(root);
    }
}
sealed class Catalog:IPackageCatalogSource
{
 public List<PackageCatalogEntry> Entries {get;}=[];
 public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version,Revision="fixture",Packages=Entries};
}
