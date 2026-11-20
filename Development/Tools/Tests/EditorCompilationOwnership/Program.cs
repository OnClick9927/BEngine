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

new Probe(args[0], Path.GetFullPath(args[1])).Run();

sealed class Probe(string mode, string output)
{
    readonly List<string> checks = [];
    void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
    void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
    static T Worker<T>(Func<T> action) => Task.Run(action).GetAwaiter().GetResult();
    static bool Rejected<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }
    public void Run()
    {
        Directory.CreateDirectory(output);
        var root=Path.Combine("E:/Project/_BZP/BEngine/Temp/SC42",Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root,"Assets")); Directory.CreateDirectory(Path.Combine(root,"Packages"));
        File.WriteAllText(Path.Combine(root,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: Shader Ownership\n");
        File.WriteAllText(Path.Combine(root,"Packages/manifest.yaml"),"packages: []\n");
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",root);
        Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH",Path.Combine(root,"Hub/Packages"));
        DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
        using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
            .AddBEngineEditorCompilation().AddBEngineEditorCompilation().AddSingleton<IPackageCatalogSource>(new EmptyCatalog())
            .AddSingleton<IShaderCompiler, BytesCompiler>().BuildServiceProvider(new ServiceProviderOptions {ValidateScopes=true,ValidateOnBuild=true});
        PlatformRuntime.Initialize(services);using var attached=EditorServiceScope.Attach(services);
        var workspace=ProjectWorkspace.Open(root);
        Check(typeof(ProjectShaderCompilationSession).Assembly==typeof(ProjectScriptHostSession).Assembly,"Compilation coordination belongs to the Compilation implementation assembly");
        Check(!typeof(ProjectScriptHostSession).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a=>a.AssemblyName.Split(',')[0]==GetType().Assembly.GetName().Name),"The consumer has no friend access");
        Check(services.GetService<IEditorNativeWindowFactory>() is null,"Compilation needs no window or GPU service");
        if(mode=="Core")Core(workspace); else if(mode=="Factory")Factory(workspace,services); else if(mode=="Graphics")Graphics(workspace); else throw new ArgumentException(mode);
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.EditorWorkbench.Host" or "BEngine.Application" or "BEngine.IMGUI.Editor"),"Compilation loads no workbench, game framework or IMGUI");
        if(mode!="Graphics")Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BEngine.Graphics.Editor"),"Core mechanisms do not load the graphics compiler");
        File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,Fixture=root},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COMPILATION_OWNERSHIP_OK|"+mode+"|"+checks.Count);
    }
    void Core(ProjectWorkspace workspace)
    {
        var diSource = ProjectScriptSourceLocator.Find(workspace, typeof(IEngineServiceModule).FullName!);
        Check(diSource is not null && diSource.Contains(Path.Combine("Core", "BEngine.Core")),
            "Source navigation locates the minimal DI kernel");
        var objectSource = ProjectScriptSourceLocator.Find(workspace, typeof(GameObject).FullName!);
        Check(objectSource is not null && objectSource.Contains(Path.Combine("Packages", "Foundation")),
            "Source navigation locates the migrated object implementation");
        Throws<ArgumentNullException>(()=>new ProjectShaderCompilationSession(null!),"A session requires a workspace");
        Check(!ProjectShaderCompiler.IsShaderPath("Assets/a.probe"),"Absent extensions define no shader format");
        using(var absent=new ProjectShaderCompilationSession(workspace))
        {var empty=absent.PrepareCompilation([]).Build();Check(empty.Artifacts.Succeeded&&absent.TryApply(empty),"No compiler is a safe empty operation");}
        var compiler=new SpyCompiler();using var registration=ProjectShaderCompiler.Register(compiler);
        Check(ProjectShaderCompiler.IsShaderPath("Assets/a.probe")&&!ProjectShaderCompiler.IsShaderPath("Assets/a.cs"),"Source classification is supplied by the active compiler");
        using var session=new ProjectShaderCompilationSession(workspace);
        Throws<ArgumentNullException>(()=>session.PrepareCompilation(null!),"Null inputs are rejected");
        Throws<ArgumentException>(()=>session.PrepareCompilation([""]),"Empty input paths are rejected before compilation");
        var inputs=new[]{"Assets/a.probe","Assets/A.probe"};var request=session.PrepareCompilation(inputs);inputs[0]="Assets/mutated.probe";
        var result=Worker(()=>request.Build());
        Check(compiler.LastPaths.SequenceEqual(["Assets/a.probe"]),"Prepared inputs are snapshots with case-insensitive de-duplication");
        Check(compiler.Applied==0,"Workers never apply artifacts");
        Throws<InvalidOperationException>(()=>request.Build(),"A prepared request builds at most once");
        Check(Worker(()=>Rejected<InvalidOperationException>(()=>session.PrepareCompilation([]))),"Workers cannot prepare owner-thread state");
        Check(Worker(()=>Rejected<InvalidOperationException>(session.CancelPending)),"Workers cannot invalidate owner-thread state");
        Check(Worker(()=>Rejected<InvalidOperationException>(()=>session.TryApply(result))),"Workers cannot apply results");
        Check(Worker(()=>Rejected<InvalidOperationException>(session.Dispose)),"Workers cannot dispose the owning session");
        Check(session.TryApply(result)&&compiler.Applied==1,"The owner applies a completed result once");
        Check(!session.TryApply(result)&&compiler.Applied==1,"Repeated application has no side effects");
        var old=session.PrepareCompilation(["Assets/old.probe"]).Build();
        var obsolete=session.PrepareCompilation(["Assets/skipped.probe"]);var latest=session.PrepareCompilation(["Assets/new.probe"]);
        Throws<InvalidOperationException>(()=>obsolete.Build(),"A superseded request cannot start building");
        Check(!session.TryApply(old),"A newer request invalidates older artifacts");
        var current=latest.Build();using(var other=new ProjectShaderCompilationSession(workspace))
            Throws<ArgumentException>(()=>other.TryApply(current),"Results cannot be applied by a different session");
        Check(session.TryApply(current),"A rejected foreign apply does not consume the rightful result");
        using(var cancel=new CancellationTokenSource())
        {
            var pre=session.PrepareCompilation(["Assets/pre.probe"]);cancel.Cancel();
            Throws<OperationCanceledException>(()=>pre.Build(cancel.Token),"Pre-cancelled work never starts");
            Check(session.TryApply(pre.Build()),"Pre-cancellation does not consume an unstarted request");
        }
        using(var cancel=new CancellationTokenSource())
        {var done=session.PrepareCompilation(["Assets/cancel.probe"]).Build(cancel.Token);cancel.Cancel();Check(!session.TryApply(done),"Cancellation after build prevents application");}
        var invalidated=session.PrepareCompilation(["Assets/invalidate.probe"]).Build();session.CancelPending();
        Check(!session.TryApply(invalidated),"Explicit cancellation invalidates already completed work");
        compiler.FailBuild=true;Throws<InvalidOperationException>(()=>session.PrepareCompilation(["Assets/fail.probe"]).Build(),"Compiler failures reach the caller");compiler.FailBuild=false;
        Check(session.TryApply(session.PrepareCompilation(["Assets/retry.probe"]).Build()),"Compiler failure releases the serial gate for subsequent work");
        var applyFailure=session.PrepareCompilation(["Assets/apply.probe"]).Build();compiler.FailApply=true;
        Throws<InvalidOperationException>(()=>session.TryApply(applyFailure),"Apply failures reach the owner");compiler.FailApply=false;
        Check(!session.TryApply(applyFailure),"A partially applied failing result cannot be retried");
        var captured=session.PrepareCompilation(["Assets/registration.probe"]);
        using(var replacement=ProjectShaderCompiler.Register(new SpyCompiler()))
            Throws<InvalidOperationException>(()=>captured.Build(),"Preparation captures compiler identity rather than silently using a replacement");
        var retired=session.PrepareCompilation(["Assets/retired.probe"]).Build();
        using(var replacement=ProjectShaderCompiler.Register(new SpyCompiler()))
            Check(!session.TryApply(retired),"A result from the displaced registration cannot publish artifacts");
        Check(!session.TryApply(retired),"Restoring an earlier compiler cannot resurrect an already rejected result");
        Concurrent(workspace,compiler);
        var final=session.PrepareCompilation(["Assets/end.probe"]).Build();session.Dispose();session.Dispose();
        Check(!session.TryApply(final),"Retired sessions reject late posted results without applying them");
        Throws<ObjectDisposedException>(()=>session.PrepareCompilation([]),"Disposed sessions reject new requests");
        var weak=Collectible(workspace);
        for(var i=0;i<16&&weak.Any(w=>w.IsAlive);i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Thread.Sleep(20);}
        Check(weak.All(w=>!w.IsAlive),"Retired requests and results do not pin a collectible compiler or its load context");
    }
    void Concurrent(ProjectWorkspace workspace,SpyCompiler compiler)
    {
        using(var session=new ProjectShaderCompilationSession(workspace))
        using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var queued=new ManualResetEventSlim())
        {
            compiler.Block=()=>{entered.Set();if(!release.Wait(10000))throw new TimeoutException();};
            var first=session.PrepareCompilation(["Assets/first.probe"]);var running=Task.Run(()=>first.Build());
            Check(entered.Wait(10000),"A worker reaches the registered compiler");
            var second=session.PrepareCompilation(["Assets/second.probe"]);var waiting=Task.Run(()=>{queued.Set();return second.Build();});
            Check(queued.Wait(10000)&&!waiting.Wait(100),"The next request waits behind an executing compiler");
            compiler.Block=null;release.Set();var a=running.GetAwaiter().GetResult();var b=waiting.GetAwaiter().GetResult();
            Check(compiler.MaxActive==1,"A session serializes compiler invocations");
            Check(!session.TryApply(a)&&session.TryApply(b),"Only the newest concurrent result may publish");
        }
        using(var session=new ProjectShaderCompilationSession(workspace))
        using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var queued=new ManualResetEventSlim())
        {
            compiler.Block=()=>{entered.Set();if(!release.Wait(10000))throw new TimeoutException();};
            var first=session.PrepareCompilation(["Assets/dispose.probe"]);var running=Task.Run(()=>Rejected<OperationCanceledException>(()=>first.Build()));
            Check(entered.Wait(10000),"Retirement fixture holds an executing extension");
            var second=session.PrepareCompilation(["Assets/queued.probe"]);var waiting=Task.Run(()=>{queued.Set();try{second.Build();return false;}catch(Exception e)when(e is OperationCanceledException or ObjectDisposedException){return true;}});
            Check(queued.Wait(10000)&&!waiting.Wait(100),"Retirement fixture also holds a waiting build");
            session.Dispose();Check(!running.IsCompleted,"Owner disposal does not wait for blocked extension code");
            compiler.Block=null;release.Set();Check(running.GetAwaiter().GetResult()&&waiting.GetAwaiter().GetResult(),"Retirement cancels executing and queued builds without a disposed-gate failure");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static WeakReference[] Collectible(ProjectWorkspace workspace)
    {
        var context=new SharedContext();var assembly=context.LoadFromAssemblyPath(typeof(SpyCompiler).Assembly.Location);
        var compiler=(IProjectShaderCompiler)Activator.CreateInstance(assembly.GetType(nameof(SpyCompiler),true)!)!;
        var registration=ProjectShaderCompiler.Register(compiler);var session=new ProjectShaderCompilationSession(workspace);
        var request=session.PrepareCompilation(["Assets/collect.probe"]);var result=request.Build();
        registration.Dispose();session.Dispose();context.Unload();
        // Deliberately keep the retired request and result alive through GC in the caller.
        Retained=[request,result];return [new(compiler),new(assembly),new(context)];
    }
    static object[]? Retained;
    void Factory(ProjectWorkspace workspace,IServiceProvider services)
    {
        var factory=services.GetRequiredService<IProjectPackageManagerFactory>();
        Check(typeof(IProjectPackageManagerFactory).Assembly==typeof(BPackageManager).Assembly,"Package manager composition belongs to the Core owner");
        Check(ReferenceEquals(factory,services.GetRequiredService<IProjectPackageManagerFactory>()),"Repeated composition registration keeps a singleton stateless factory");
        var compilerType=typeof(BPackageManager).Assembly.GetType("BEngine.ProjectSystem.IPackageSourceCompiler",true)!;
        var field=factory.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Single(f=>f.FieldType==compilerType);
        Check(ReferenceEquals(field.GetValue(factory),services.GetRequiredService(compilerType)),"The factory uses the compiler from its host provider");
        using(var empty=factory.Create(workspace,new BPackageCatalog([])))
        {Check(empty.packages.Count==0&&empty.definitions.Count==0,"An explicitly empty catalog opens without repository discovery");}
        var manifestPath=Path.Combine(workspace.RootPath,"Packages/manifest.yaml");
        Check(File.Exists(manifestPath),"Creation retains the existing project manifest format");
        var definition=Path.Combine(workspace.RootPath,"Hub/Packages/Fixture/Fixture-1.0.0/package.yaml");Directory.CreateDirectory(Path.GetDirectoryName(definition)!);
        new PackageDefinitionDocument{Id="com.fixture.compileownership",DisplayName="Compilation Fixture",MinimumEngineVersion=EngineVersion.Current,Runtime=new(){Assembly="SC42.Package",RootNamespace="SC42"}}.Save(definition);
        var folder=Path.Combine(Path.GetDirectoryName(definition)!,"SC42.Package");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Runtime.cs"),"namespace SC42; public sealed class PackageMarker : BEngine.BAsset { public const int Value=42; }");
        File.WriteAllText(Path.Combine(folder,"SC42.Package.asmdef.yaml"),YamlUtility.Serialize(new AssemblyDefinitionDocument{Name="SC42.Package",RootNamespace="SC42"}));
        File.WriteAllText(manifestPath,"packages:\n- id: com.fixture.compileownership\n  version: 1.0.0\n  enabled: true\n");
        using(var real=factory.Create(workspace,new BPackageCatalog([definition])))
        {
            Check(real.IsEnabled("com.fixture.compileownership"),"The created manager activates the selected package");
            Check(RuntimeTypeCache.GetAllTypes().Any(t=>t.FullName=="SC42.PackageMarker"),"Core's factory compiles and loads actual package source");
            Check(real.definitions.Single().Document.Id=="com.fixture.compileownership","Explicit catalogs preserve package identity");
        }
        Check(!RuntimeTypeCache.GetAllTypes().Any(t=>t.FullName=="SC42.PackageMarker"),"The caller's disposal retires the created manager's package types");
        var custom=new ManifestFactory();using var overridden=new ServiceCollection().AddSingleton<IProjectPackageManagerFactory>(custom)
            .AddBEnginePlatformServices().AddBEngineEditorPlatformServices().AddBEngineEditorCompilation().BuildServiceProvider();
        Check(ReferenceEquals(custom,overridden.GetRequiredService<IProjectPackageManagerFactory>()),"A host may replace the factory without exposing internal compilers");
        using(var manifest=overridden.GetRequiredService<IProjectPackageManagerFactory>().Create(workspace,new BPackageCatalog([definition])))
            Check(custom.Calls==1&&manifest.IsEnabled("com.fixture.compileownership"),"Custom composition can choose an authoring-only manager");
        Check(!RuntimeTypeCache.GetAllTypes().Any(t=>t.FullName=="SC42.PackageMarker"),"The custom manifest factory does not activate package code");
    }
    void Graphics(ProjectWorkspace workspace)
    {
        var graphics=Assembly.Load("BEngine.Graphics.Editor");
        RuntimeHelpers.RunModuleConstructor(graphics.ManifestModule.ModuleHandle);
        Check(ProjectShaderCompiler.IsShaderPath("Assets/a.frag.cg"),"The real Graphics adapter supplies shader source formats");
        Directory.CreateDirectory(workspace.AssetsPath);var path=Path.Combine(workspace.AssetsPath,"Sample.frag.cg");
        File.WriteAllText(path,"#pragma stage fragment\nfloat4 main() : SV_Target { return float4(1,0,0,1); }\n");
        using var session=new ProjectShaderCompilationSession(workspace);
        var request=session.PrepareCompilation(["Assets/Sample.frag.cg"]);var first=Worker(()=>request.Build());
        Check(first.Artifacts.Succeeded&&first.Artifacts.CompiledArtifacts.Count==1,"The real Graphics adapter builds shader artifacts through a Core worker request");
        var artifact=first.Artifacts.CompiledArtifacts.Values.Single();
        Check(File.Exists(artifact)&&File.ReadAllBytes(artifact).SequenceEqual(new byte[]{3,2,35,7,0,0,0,0}),"The adapter writes bytes supplied by the isolated compiler backend");
        Check(ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg") is null,"Background compilation does not publish the current artifact pointer");
        Check(session.TryApply(first)&&ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg")==artifact,"Owner-thread application publishes Graphics' artifact pointer");
        var calls=BytesCompiler.Calls;var cached=session.PrepareCompilation(["Assets/Sample.frag.cg"]).Build();
        Check(cached.Artifacts.CompiledArtifacts.Values.Single()==artifact&&BytesCompiler.Calls==calls,"Unchanged sources reuse the real adapter's content-addressed cache");
        File.AppendAllText(path,"\n// changed\n");var replacement=session.PrepareCompilation(["Assets/Sample.frag.cg"]).Build();
        Check(replacement.Artifacts.CompiledArtifacts.Values.Single()!=artifact,"A changed source produces a distinct artifact identity");
        Check(ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg")==artifact,"Prepared replacements leave the previous pointer intact until application");
        Check(!session.TryApply(cached)&&session.TryApply(replacement),"An obsolete cached result cannot overwrite a newer replacement");
        File.Delete(path);var removed=session.PrepareCompilation(["Assets/Sample.frag.cg"]).Build();
        Check(removed.Artifacts.DeletedAssets.SequenceEqual(["Assets/Sample.frag.cg"]),"Graphics reports source retirement through the neutral result payload");
        Check(ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg") is not null,"A worker does not delete the published pointer");
        Check(session.TryApply(removed)&&ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg") is null,"Graphics owns pointer retirement when the result is applied");
        File.WriteAllText(path,"#pragma stage fragment\nfloat4 main() : SV_Target { return float4(0,1,0,1); }\n");BytesCompiler.Fail=true;
        var failed=session.PrepareCompilation(["Assets/Sample.frag.cg"]).Build();BytesCompiler.Fail=false;
        Check(!failed.Artifacts.Succeeded&&failed.Artifacts.Errors.Count==1&&failed.Artifacts.CompiledArtifacts.Count==0,"Backend compile errors remain asset-level result data");
        Check(session.TryApply(failed)&&ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg") is null,"Applying diagnostics cannot publish an invalid artifact");
        var recovered=session.PrepareCompilation(["Assets/Sample.frag.cg"]).Build();
        Check(recovered.Artifacts.Succeeded&&session.TryApply(recovered),"Compilation and publication recover after a backend failure");
    }
}

public sealed class SpyCompiler : IProjectShaderCompiler
{
    public int Applied,Active,MaxActive;
    public string[] LastPaths=[];
    public bool FailBuild,FailApply;
    public Action? Block;
    public bool IsShaderPath(string path)=>path.EndsWith(".probe",StringComparison.OrdinalIgnoreCase);
    public ShaderCompilationResult CompileChangedInBackground(ProjectWorkspace workspace,IEnumerable<string> paths,CancellationToken cancellationToken=default)
    {
        var count=Interlocked.Increment(ref Active);MaxActive=Math.Max(MaxActive,count);
        try
        {
            LastPaths=paths.ToArray();Block?.Invoke();if(FailBuild)throw new InvalidOperationException("fixture compile failure");
            return new(LastPaths.ToDictionary(p=>p,p=>p+".compiled"),new Dictionary<string,string>(),new Dictionary<string,string>());
        }
        finally {Interlocked.Decrement(ref Active);}
    }
    public void ApplyCompilationResult(ShaderCompilationResult result){Applied++;if(FailApply)throw new InvalidOperationException("fixture apply failure");}
    public string? ResolveCurrentArtifact(ProjectWorkspace workspace,string path)=>null;
}
sealed class SharedContext():AssemblyLoadContext(isCollectible:true)
{
    protected override Assembly? Load(AssemblyName name)=>Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==name.Name&&a!=typeof(SpyCompiler).Assembly);
}
sealed class EmptyCatalog:IPackageCatalogSource
{public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version,Revision="empty",Packages=[]};}
sealed class ManifestFactory:IProjectPackageManagerFactory
{public int Calls;public BPackageManager Create(ProjectWorkspace workspace,BPackageCatalog? catalog=null){Calls++;return BPackageManager.OpenManifest(workspace,catalog);}}
sealed class BytesCompiler:IShaderCompiler
{
    public static int Calls;public static bool Fail;
    public byte[] Compile(string source,string sourceName,ShaderCompilationStage stage,string entryPoint,bool warningsAsErrors,int optimizationLevel)
    {Calls++;if(Fail)throw new InvalidOperationException("fixture backend compile failure");return [3,2,35,7,0,0,0,0];}
}
