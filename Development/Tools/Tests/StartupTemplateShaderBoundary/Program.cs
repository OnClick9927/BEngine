using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.App.Editor.ProjectSystem;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Editor.Build.ProjectSystem;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]);var group=args[1];var legacy=Path.GetFullPath(args[2]);
var run=Path.Combine(output,group,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(run,"Hub"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var scope=EditorServiceScope.Attach(services);
var probe=new Probe(run,legacy);
switch(group){case "Core":probe.Core();break;case "Application":probe.Application(output);break;case "Platform":probe.Platform();break;default:throw new ArgumentException(group);}
File.WriteAllText(Path.Combine(output,group,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=probe.Checks.Count,Details=probe.Checks,ParityCases=probe.ParityCases,MatrixCases=probe.MatrixCases,LegacyInputs=probe.LegacyInputs},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("STARTUP_TEMPLATE_SHADER_OK|"+group+"|"+probe.Checks.Count+"|parity="+probe.ParityCases);

sealed class Probe(string run,string legacy)
{
    public List<string> Checks { get; }=[];
    public int ParityCases { get; private set; }
    public int MatrixCases { get; private set; }
    public List<object> LegacyInputs { get; }=[];
    void Check(bool ok,string name){if(!ok)throw new Exception(name);Checks.Add(name);}
    void Throws<T>(Action action,string name)where T:Exception
    {try{action();}catch(TargetInvocationException e)when(e.InnerException is T){Check(true,name);return;}catch(T){Check(true,name);return;}throw new Exception(name+" did not throw "+typeof(T).Name);}
    ProjectWorkspace Project(string name)
    {
        var root=Path.Combine(run,name);Directory.CreateDirectory(Path.Combine(root,"Assets"));Directory.CreateDirectory(Path.Combine(root,"ProjectSettings"));
        File.WriteAllText(Path.Combine(root,"Project.yaml"),YamlUtility.Serialize(new ProjectData{Name="Boundary Project",EngineVersion="0.1.0"}));
        File.WriteAllText(Path.Combine(root,"ProjectSettings/ProjectSettings.yaml"),YamlUtility.Serialize(new ProjectSettingsData{CompanyName="Boundary"}));
        var w=ProjectWorkspace.Open(root);Directory.CreateDirectory(Path.GetDirectoryName(w.StartupScenePath)!);
        using(var scene=new Scene("Main"))File.WriteAllText(w.StartupScenePath,SceneAssetDocuments.Serialize(scene));return w;
    }
    string Text(ProjectWorkspace w,string relative,string text){var p=w.ResolveInside(relative);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,text);return p;}
    static void Module(string name){var a=Assembly.Load(name);RuntimeHelpers.RunModuleConstructor(a.ManifestModule.ModuleHandle);}
    LegacyModule Old(string name)
    {
        var path=Path.Combine(legacy,name,"release",name+".dll");
        if(!File.Exists(path))throw new FileNotFoundException(path);
        LegacyInputs.Add(new{Assembly=name,Path=path,SHA256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))});
        return new(path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Core()
    {
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Application" or "BEngine.Application.Editor"),"Core policies operate without the application framework");
        Check(typeof(AotProjectLayout).GetFields(BindingFlags.Public|BindingFlags.Static).Select(f=>f.Name).Order().SequenceEqual(new[]{"AssemblyDefinitionAssetPath","AssemblyName","AssetRoot","SceneAssetPath"}),"Core AOT contract owns only assembly and scene identity");
        foreach(var path in new[]{"Assets/AOT","Assets/AOT/AOT.scene.yaml",@"Assets\AOT\UI\custom.bxml","Assets/./AOT/UI/../Custom.cs"})Check(AotProjectLayout.IsAotAssetPath(path),"AOT partition still includes developer content: "+path);
        foreach(var path in new[]{"Assets/AOT","Assets","Assets/AOT/AOT.scene.yaml"})Check(AotProjectLayout.IsProtectedAssetPath(path),"Reserved AOT identity remains protected: "+path);
        foreach(var path in new[]{"Assets/AOT/UI/custom.bxml","Assets/AOT/AOT.asmdef.yaml","Assets/AOT-sibling",""})Check(!AotProjectLayout.IsProtectedAssetPath(path),"Other content is not an engine-owned protected identity: "+path);
        var w=Project("Core");var settings=PlayerBuildSettingsStore.Load(w.RootPath);
        Check(settings.Scenes.Single().Path==AotProjectLayout.SceneAssetPath,"Core keeps the bootstrap scene default without supplying UI");
        Text(w,"Assets/Aot/BEngine.png","legacy");Text(w,"Assets/AOT/UI/BEngine.png","template");
        settings.SplashImage="Assets/Aot/BEngine.png";PlayerBuildSettingsStore.Save(w.RootPath,settings);
        Check(PlayerBuildSettingsStore.Load(w.RootPath).SplashImage=="Assets/Aot/BEngine.png","Core does not infer framework-specific logo migration");
        Text(w,"Assets/Custom/Startup.png","custom");var calls=0;
        void Migrate(ProjectWorkspace workspace,PlayerBuildSettings value){if(workspace.RootPath!=w.RootPath)throw new Exception("Wrong project");calls++;value.SplashImage="Assets/Custom/Startup.png";}
        PlayerProjectPolicies.NormalizeBuildSettings+=Migrate;
        try
        {
            settings.SplashImage="unavailable.png";PlayerBuildSettingsStore.Save(w.RootPath,settings);
            Check(PlayerBuildSettingsStore.Load(w.RootPath).SplashImage=="Assets/Custom/Startup.png"&&calls==2,"A custom workflow migrates settings on both save and load through Core");
            settings.Version=999;var before=calls;Throws<InvalidDataException>(()=>PlayerBuildSettingsStore.Save(w.RootPath,settings),"Foreign settings schemas fail before workflow migration");Check(calls==before,"Invalid schemas are never delivered to contributors");settings.Version=PlayerBuildSettings.CurrentVersion;
        }
        finally{PlayerProjectPolicies.NormalizeBuildSettings-=Migrate;}
        settings.SplashImage="Assets/Aot/BEngine.png";PlayerBuildSettingsStore.Save(w.RootPath,settings);Check(PlayerBuildSettingsStore.Load(w.RootPath).SplashImage==settings.SplashImage,"Removing a contributor restores unopinionated Core behavior");
        void Invalid(ProjectWorkspace _,PlayerBuildSettings value)=>value.SplashBackgroundColor="invalid";
        PlayerProjectPolicies.NormalizeBuildSettings+=Invalid;
        var disk=File.ReadAllBytes(Path.Combine(w.ProjectSettingsPath,PlayerBuildSettingsStore.FileName));
        try{Throws<InvalidDataException>(()=>PlayerBuildSettingsStore.Save(w.RootPath,settings),"Core validates settings after a custom migration");}
        finally{PlayerProjectPolicies.NormalizeBuildSettings-=Invalid;settings.SplashBackgroundColor="#10161A";}
        Check(File.ReadAllBytes(Path.Combine(w.ProjectSettingsPath,PlayerBuildSettingsStore.FileName)).SequenceEqual(disk),"Failed migration validation preserves settings on disk");
        void Fail(ProjectWorkspace _,PlayerBuildSettings __)=>throw new IOException("migration failure");
        PlayerProjectPolicies.NormalizeBuildSettings+=Fail;
        try{Throws<IOException>(()=>PlayerBuildSettingsStore.Save(w.RootPath,settings),"Contributor failures reach the caller without a partial save");}finally{PlayerProjectPolicies.NormalizeBuildSettings-=Fail;}
        Check(File.ReadAllBytes(Path.Combine(w.ProjectSettingsPath,PlayerBuildSettingsStore.FileName)).SequenceEqual(disk),"Contributor failures leave the persisted settings intact");
        Throws<ArgumentNullException>(()=>PlayerProjectPolicies.NormalizeSettings(null!,settings),"The generic hook rejects a missing workspace");
        Throws<ArgumentNullException>(()=>PlayerProjectPolicies.NormalizeSettings(w,null!),"The generic hook rejects missing settings");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Application" or "BEngine.Application.Editor"),"Core checks never load the application package");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Application(string output)
    {
        Module("BEngine.Application.Editor");
        var icons=Path.Combine(output,"Editor/Icons");Directory.CreateDirectory(icons);
        File.WriteAllBytes(Path.Combine(icons,"BEngine.png"),Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAnkAAAAASUVORK5CYII="));
        var current=Project("Current");new PlayerProjectTemplate().Initialize(current);
        var beforeMeta=Directory.GetFiles(current.ResolveInside(AotProjectLayout.AssetRoot),"*.meta",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(current.RootPath,p),File.ReadAllBytes);
        PlayerProjectTemplate.EnsureRequiredAotInvariants(current);
        Check(beforeMeta.All(p=>File.ReadAllBytes(current.ResolveInside(p.Key)).SequenceEqual(p.Value)),"Repeated framework repair preserves every existing resource GUID and metadata byte");
        Check(PlayerBuildSettingsStore.Load(current.RootPath).SplashImage==AotTemplateLayout.LogoAssetPath,"Framework default settings select its own logo");
        using(var old=Old("BEngine.Application.Editor"))
        {
            var template=old.Assembly.GetType("BEngine.App.Editor.ProjectSystem.PlayerProjectTemplate",true)!;
            var previous=Project("Previous");template.GetMethod("Initialize")!.Invoke(Activator.CreateInstance(template),[previous]);
            var files=Directory.GetFiles(current.ResolveInside(AotProjectLayout.AssetRoot),"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".meta",StringComparison.Ordinal)).ToArray();
            foreach(var file in files)
            {
                var relative=Path.GetRelativePath(current.RootPath,file);Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(previous.ResolveInside(relative))),"Generated asset equals the real previous template: "+relative);ParityCases++;
            }
            Check(Directory.GetFiles(previous.ResolveInside(AotProjectLayout.AssetRoot),"*",SearchOption.AllDirectories).Length==Directory.GetFiles(current.ResolveInside(AotProjectLayout.AssetRoot),"*",SearchOption.AllDirectories).Length,"Template migration neither adds nor drops assets");
        }
        var values=PlayerBuildSettingsStore.Load(current.RootPath);values.SplashImage=@"  assets\aot\BEngine.png  ";PlayerBuildSettingsStore.Save(current.RootPath,values);
        Check(values.SplashImage==AotTemplateLayout.LogoAssetPath&&PlayerBuildSettingsStore.Load(current.RootPath).SplashImage==values.SplashImage,"Framework migrates legacy logo paths before engine asset validation");
        Text(current,"Assets/Custom.png","custom");values.SplashImage="Assets/Custom.png";PlayerBuildSettingsStore.Save(current.RootPath,values);Check(PlayerBuildSettingsStore.Load(current.RootPath).SplashImage==values.SplashImage,"Explicit developer startup images are preserved");
        // Exercise the actual legacy resource move and preserve the original meta identities.
        var uiNames=new[]{"AOT.bxml","AOT.bss","AotStartupView.cs","BEngine.png"};
        foreach(var name in uiNames)
        {
            var from=current.ResolveInside(AotTemplateLayout.UiAssetRoot+"/"+name);var to=current.ResolveInside(AotProjectLayout.AssetRoot+"/"+name);File.Move(from,to);File.Move(from+".meta",to+".meta");
        }
        var scene=current.ResolveInside(AotProjectLayout.SceneAssetPath);File.WriteAllText(scene,File.ReadAllText(scene).Replace(AotTemplateLayout.UiDocumentAssetPath,"Assets/Aot/AOT.bxml",StringComparison.Ordinal));
        PlayerProjectPolicies.Ensure(current);
        Check(uiNames.All(name=>File.Exists(current.ResolveInside(AotTemplateLayout.UiAssetRoot+"/"+name))&&!File.Exists(current.ResolveInside(AotProjectLayout.AssetRoot+"/"+name))),"Framework moves its old UI resources into the existing UI folder");
        Check(beforeMeta.All(p=>File.ReadAllBytes(current.ResolveInside(p.Key)).SequenceEqual(p.Value)),"Legacy moves preserve GUIDs and asset metadata");
        Check(File.ReadAllText(scene).Contains(AotTemplateLayout.UiDocumentAssetPath,StringComparison.Ordinal)&&!File.ReadAllText(scene).Contains("Assets/Aot/AOT.bxml",StringComparison.Ordinal),"Framework updates old scene UI references");
        values.SplashImage="Assets/Aot/BEngine.png";PlayerProjectPolicies.NormalizeSettings(current,values);Check(values.SplashImage==AotTemplateLayout.LogoAssetPath,"The active framework contributes its legacy settings migration");
        RuntimeAssemblyCleanup.UnregisterExtensions(typeof(PlayerProjectTemplate).Assembly);
        values.SplashImage="Assets/Aot/BEngine.png";PlayerProjectPolicies.NormalizeSettings(current,values);Check(values.SplashImage=="Assets/Aot/BEngine.png","Unloading the framework releases its settings callback");
        var noUi=Project("CustomAot");Text(noUi,AotProjectLayout.AssemblyDefinitionAssetPath,YamlUtility.Serialize(new BEngine.Editor.Documents.AssemblyDefinitionDocument{Name="AOT",AutoReferenced=false}));
        PlayerProjectTemplate.EnsureRequiredAotInvariants(noUi);
        Check(!Directory.Exists(noUi.ResolveInside(AotTemplateLayout.UiAssetRoot)),"Existing custom AOT projects do not receive framework UI");
        Check(File.ReadAllText(noUi.ResolveInside(AotProjectLayout.SceneAssetPath)).Contains("gameObjects: []",StringComparison.Ordinal),"A custom bootstrap can remain an empty engine scene");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Platform()
    {
        using(var old=Old("BEngine.Player.Editor"))
        {
            var policy=old.Assembly.GetType("BEngine.Editor.Build.PlayerGraphicsBuildSettings",true)!;
            foreach(var target in BuildTargetUtility.Current.All)
            {
                var expected=(IReadOnlyList<GraphicsBackend>)policy.GetMethod("GetSupported")!.Invoke(null,[target,null])!;
                Check(expected.SequenceEqual(PlatformGraphicsBuildPolicy.GetSupportedBackends(target))&&expected.SequenceEqual(PlayerGraphicsBuildSettings.GetSupported(target)),"Host backend capabilities match the previous implementation: "+target.TargetId);ParityCases++;
            }
            foreach(var selected in new IReadOnlyList<GraphicsBackend>[] {[],[GraphicsBackend.OpenGL],[GraphicsBackend.Vulkan],[GraphicsBackend.OpenGL,GraphicsBackend.Vulkan],[GraphicsBackend.WebGL]})
            {
                var expected=(string)policy.GetMethod("DesktopVariant")!.Invoke(null,[selected])!;
                Check(PlatformGraphicsBuildPolicy.DesktopVariant(selected)==expected&&PlayerGraphicsBuildSettings.DesktopVariant(selected)==expected,"Desktop linking variant parity: "+string.Join(',',selected));ParityCases++;
            }
        }
        var matrix=0;
        foreach(var selected in Enum.GetValues<GraphicsBackend>().Select(b=>new[]{b}).Concat(new[]{new[]{GraphicsBackend.OpenGL,GraphicsBackend.Vulkan},Array.Empty<GraphicsBackend>()}))
        foreach(var compile in new[]{false,true})foreach(var runtime in new[]{false,true})
        {
            var vulkan=selected.Contains(GraphicsBackend.Vulkan);
            var offline=PlatformGraphicsBuildPolicy.GetOfflineBackends(selected,compile);
            if(!offline.SequenceEqual(vulkan&&compile?new[]{GraphicsBackend.Vulkan}:Array.Empty<GraphicsBackend>()))throw new Exception("Offline selection changed");
            if(PlatformGraphicsBuildPolicy.IncludesRuntimeCompiler(selected,runtime)!=(vulkan&&runtime))throw new Exception("Runtime compiler selection changed");
            try{PlatformGraphicsBuildPolicy.Validate(selected,compile,runtime);if(vulkan&&!compile&&!runtime)throw new Exception("Missing compiler accepted");}
            catch(InvalidDataException e){if(!vulkan||compile||runtime||e.Message!="Vulkan without a runtime compiler requires Compile Shaders.")throw;}
            matrix++;
        }
        MatrixCases=matrix;Check(matrix==44,"All built-in backend/offline/runtime option combinations preserve their prior requirements");
        var immutable=PlatformGraphicsBuildPolicy.GetOfflineBackends(new[]{GraphicsBackend.Vulkan},true);
        Throws<NotSupportedException>(()=>((IList<GraphicsBackend>)immutable)[0]=GraphicsBackend.OpenGL,"Consumers cannot mutate the shared offline backend selection");
        Throws<ArgumentNullException>(()=>PlatformGraphicsBuildPolicy.GetOfflineBackends(null!,true),"A missing backend selection is rejected");
        Module("BEngine.Graphics.Editor");Module("BEngine.AssetBundles.Editor");
        using var compiler=ShaderBuildPipeline.RegisterCompiler(GraphicsBackend.Vulkan,(source,_)=>SHA256.HashData(Encoding.UTF8.GetBytes(source.Code)));
        var file=Path.Combine(run,"test.vulkan.vert.glsl");File.WriteAllText(file,"#version 450\nvoid main(){gl_Position=vec4(0);}");
        var bytes=File.ReadAllBytes(file);var resource=new RuntimePackageResourceInput("probe","Assets/Probe/test.vulkan.vert.glsl",file,Convert.ToHexStringLower(SHA256.HashData(bytes)),bytes.Length);
        var input=new RuntimeManagedCodeReleaseInputSet([],new[]{resource,resource},Array.Empty<string>());
        var type=Assembly.Load("BEngine.AssetBundles.Editor").GetType("BEngine.Editor.Build.PlayerShaderBuildPipeline",true)!;
        var add=type.GetMethod("AddArtifacts",BindingFlags.Static|BindingFlags.NonPublic)!;
        var targetDesktop=BuildTargetUtility.Current.Resolve(BEngine.Build.BuildTargetPlatform.Windows);
        PlayerBuildContext Context(string directory,GraphicsBackend[] selected,bool compile)=>new(new PlayerBuildRequest{CompileShaders=compile},targetDesktop,Path.Combine(run,directory),null){GraphicsBackends=selected,StrippingReport=new()};
        RuntimeManagedCodeReleaseInputSet Apply(MethodInfo method,PlayerBuildContext context,CancellationToken token=default)=>(RuntimeManagedCodeReleaseInputSet)method.Invoke(null,[context,input,Array.Empty<AssetRecord>(),token])!;
        using(var old=Old("BEngine.AssetBundles.Editor"))
        {
            var oldAdd=old.Assembly.GetType(type.FullName!,true)!.GetMethod("AddArtifacts",BindingFlags.Static|BindingFlags.NonPublic)!;
            foreach(var selected in new[]{new[]{GraphicsBackend.OpenGL},new[]{GraphicsBackend.Vulkan},new[]{GraphicsBackend.OpenGL,GraphicsBackend.Vulkan}})
            foreach(var enabled in new[]{false,true})
            {
                var a=Context("New-"+Guid.NewGuid().ToString("N"),selected,enabled);var b=Context("Old-"+Guid.NewGuid().ToString("N"),selected,enabled);
                var current=Apply(add,a);var before=Apply(oldAdd,b);
                string[] Contents(RuntimeManagedCodeReleaseInputSet result)=>result.PackageResources.Select(r=>r.Address+"|"+r.PackageId+"|"+r.Sha256+"|"+r.Size+"|"+Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(r.FilePath)))).ToArray();
                Check(Contents(current).SequenceEqual(Contents(before))&&a.StrippingReport!.OfflineShaderCount==b.StrippingReport!.OfflineShaderCount&&a.StrippingReport.OfflineShaderBytes==b.StrippingReport.OfflineShaderBytes,"Real previous AB pipeline output/report parity: "+string.Join(',',selected)+" offline="+enabled);ParityCases++;
                if(enabled&&selected.Contains(GraphicsBackend.Vulkan))Check(current.PackageResources.Count==3&&a.StrippingReport!.OfflineShaderCount==1,"Archive adapters deduplicate the compiled variant");
                else Check(ReferenceEquals(current,input),"A source-only build preserves the original input set");
            }
        }
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Throws<OperationCanceledException>(()=>Apply(add,Context("Cancelled",[GraphicsBackend.Vulkan],true),canceled.Token),"Shader archive adaptation propagates cancellation");
        using(var invalid=ShaderBuildPipeline.RegisterCompiler(GraphicsBackend.Vulkan,(_,_)=>[]))
            Throws<InvalidDataException>(()=>Apply(add,Context("EmptyCompiler",[GraphicsBackend.Vulkan],true)),"Invalid platform compiler output fails before archive insertion");
    }
}

sealed class LegacyModule : AssemblyLoadContext,IDisposable
{
    public Assembly Assembly { get; }
    public LegacyModule(string path):base("Previous-"+Guid.NewGuid().ToString("N"),isCollectible:true)=>Assembly=LoadFromAssemblyPath(path);
    protected override Assembly? Load(AssemblyName name)=>Default.Assemblies.FirstOrDefault(a=>AssemblyName.ReferenceMatchesDefinition(a.GetName(),name))??Default.LoadFromAssemblyName(name);
    public void Dispose(){RuntimeAssemblyCleanup.UnregisterExtensions(Assembly);RuntimeServiceContributions.UnregisterAssembly(Assembly);Unload();}
}
