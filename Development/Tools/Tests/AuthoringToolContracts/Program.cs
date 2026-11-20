using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Serialization;

new Probe(args[0], Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).Run();

sealed class Probe(string mode, string output, string previous)
{
    readonly List<string> checks = [];
    int parityCases;
    void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    void Throws<T>(Action call, string name) where T : Exception
    { try { call(); } catch (T) { Check(true, name); return; } throw new InvalidOperationException(name); }
    public void Run()
    {
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
        DotNetPlatformServices.Initialize();
        DotNetEditorPlatform.Initialize();
        Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == typeof(Probe).Assembly.GetName().Name), "Consumer has no Core internal-access grant");
        switch (mode)
        {
            case "Paths": Paths(); break;
            case "Templates": Templates(); break;
            case "References": References(); break;
            case "Compilation": Compilation(); break;
            default: throw new ArgumentException(mode);
        }
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Application"), "Contract works without graphics, workbench or application packages");
        File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed=true, Checks=checks.Count, Names=checks, PreviousBinaryComparisonCases=parityCases }, new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine($"AUTHORING_TOOL_{mode.ToUpperInvariant()}_OK {checks.Count}");
    }
    void Paths()
    {
        var files = EditorServices.GetRequiredService<FileUtility>(); var paths = EditorServices.GetRequiredService<PathUtility>();
        var project = Path.Combine(output, "Project"); Directory.CreateDirectory(project);
        var untouched = Path.Combine(output, "Uncreated");
        Check(PlayerBuildLayout.GetDefaultBuildRoot(untouched, paths) == Path.Combine(untouched,"Build") && PlayerBuildLayout.GetDefaultHotResourceRoot(untouched,paths) == Path.Combine(untouched,"Build","hotres") && !Directory.Exists(untouched), "Default addresses are computed without creating folders");
        Check(PlayerBuildLayout.GetDefaultBuildRoot(project) == PlayerBuildLayout.GetDefaultBuildRoot(project,paths) && PlayerBuildLayout.GetDefaultHotResourceRoot(project) == PlayerBuildLayout.GetDefaultHotResourceRoot(project,paths), "Host and explicit path services agree");
        Throws<ArgumentException>(()=>PlayerBuildLayout.GetDefaultBuildRoot(" ",paths), "Blank project root rejected");
        Throws<ArgumentNullException>(()=>PlayerBuildLayout.GetDefaultHotResourceRoot(project,null!), "Null path service rejected");
        using var old = new SharedContext();
        var legacyPath = Path.Combine(previous,"BEngine.Player.Editor/release/BEngine.Player.Editor.dll");
        var type = old.LoadFromAssemblyPath(legacyPath).GetType(typeof(PlayerBuildLayout).FullName!,true)!;
        var method = type.GetMethod("ValidateOutputLocation",BindingFlags.Static|BindingFlags.NonPublic)!;
        var candidates = new[] { project, output, Path.Combine(project,"Assets","x"), Path.Combine(project,"Library","x"), Path.Combine(project,"Build"), Path.Combine(project,"Build","Windows"), Path.Combine(output,"Outside"), project+"Sibling", Path.Combine(project,"BuildOther") };
        foreach (var candidate in candidates)
        {
            var current = Failure(()=>PlayerBuildLayout.ValidateOutputLocation(project,candidate,files,paths));
            var before = Failure(()=>method.Invoke(null,[project,candidate,files,paths]));
            Check(current == before, $"Output guard agrees with previous binary: {Path.GetRelativePath(output,candidate)}"); parityCases++;
        }
        var link = Path.Combine(output,"LinkedBuild");
        Check(Directory.Exists(link) && (File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0, "Linked-output fixture is a real junction");
        Throws<InvalidOperationException>(()=>PlayerBuildLayout.ValidateOutputLocation(project,Path.Combine(link,"Game"),files,paths), "An ancestor junction cannot bypass output protection");
        Check(!Directory.Exists(Path.Combine(project,"Build")) && !Directory.Exists(Path.Combine(output,"Outside")), "Preflight does not create output folders");
        foreach (var version in new[] {"v1","v21474836470","v01","V1","v0","v-1","v1 ","","../v1"})
        {
            var valid = version is "v1" or "v21474836470";
            Check((Failure(()=>BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(version)) is null)==valid,$"Existing public version contract: '{version}'");
        }
        File.WriteAllText(Path.Combine(output,"LegacyInput.json"), JsonSerializer.Serialize(new { Path=legacyPath,SHA256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(legacyPath))) }));
    }
    static string? Failure(Action call)
    {
        try { call(); return null; }
        catch (TargetInvocationException e) when(e.InnerException is not null) { return e.InnerException.GetType().FullName+":"+e.InnerException.Message; }
        catch (Exception e) { return e.GetType().FullName+":"+e.Message; }
    }
    void Templates()
    {
        var context = new PackageEditorTemplateContext("Sample","Studio.Sample","SampleWindow");
        var fallback = PackageEditorTemplates.Create(context);
        Check(fallback.Source.Contains("namespace Studio.Sample.Editor;") && fallback.Source.Contains("Tools/Sample") && fallback.AssemblyReferences.Length==0 && fallback.Dependencies.Length==0,"Default template is headless and carries no UI package dependency");
        PackageEditorTemplateContext observed=default;
        PackageEditorTemplates.Register(value=>{observed=value; return new("custom",["Studio.Editor"],[new(){PackageId="com.studio.tools",Target="editor"}]);});
        var custom=PackageEditorTemplates.Create(context);
        Check(observed==context && custom.Source=="custom" && custom.AssemblyReferences.SequenceEqual(["Studio.Editor"]) && custom.Dependencies.Single().PackageId=="com.studio.tools", "Registered provider receives context and supplies source and dependencies together");
        PackageEditorTemplates.Register(_=>new("replacement",[],[]));
        Check(PackageEditorTemplates.Create(context).Source=="replacement", "Re-registering an owner replaces its provider");
        PackageEditorTemplates.Register(_=>throw new InvalidDataException("fixture provider"));
        Throws<InvalidDataException>(()=>PackageEditorTemplates.Create(context), "Provider failure reaches the authoring transaction");
        Throws<ArgumentNullException>(()=>PackageEditorTemplates.Register(null!), "Null provider rejected");
    }
    void References()
    {
        var source=Path.Combine(output,"Nonexistent.png"); var cache=BAsset.cacheGeneration;
        var asset=DefaultAsset.CreateReadOnlyReference("Image","Packages/Test/Image.png",source,"texture","com.test.package","1.0.0");
        var again=DefaultAsset.CreateReadOnlyReference("Image","Packages/Test/Image.png",source,"texture");
        try
        {
            Check(asset.name=="Image" && asset.assetPath=="Packages/Test/Image.png" && asset.sourcePath==source && asset.assetType=="texture", "Reference preserves display, logical and physical paths and type");
            Check(asset.packageId=="com.test.package" && asset.packageVersion=="1.0.0" && again.packageId=="" && again.packageVersion=="", "Owner supplies package identity and null defaults");
            Check(asset.hideFlags==(HideFlags.NotEditable|HideFlags.DontSaveInEditor) && asset.guid=="" && asset.artifactPath=="", "Reference remains read-only, unsaved and unimported");
            Check(!ReferenceEquals(asset,again) && asset.GetInstanceID()!=again.GetInstanceID() && BAsset.cacheGeneration==cache && !File.Exists(source), "Creating references neither reuses objects nor reads/imports a nonexistent source");
            Throws<ArgumentNullException>(()=>DefaultAsset.CreateReadOnlyReference("x",null!,source,"texture"), "Null address rejected");
            Check(typeof(BAsset).GetProperty(nameof(BAsset.sourcePath))!.SetMethod?.IsPublic!=true && typeof(DefaultAsset).GetProperty(nameof(DefaultAsset.packageId))!.SetMethod?.IsPublic!=true, "Metadata setters remain nonpublic");
            using var scene=new Scene("Edit"); var root=scene.CreateGameObject("root"); var runtime=new SceneRuntime(scene);
            Check(!RuntimeAssetReferences.IsTransient(root), "Editable hierarchy can be used by the Prefab tool");
            runtime.Start();
            try { Check(RuntimeAssetReferences.IsTransient(root), "Objects in a running scene are protected without reading a private flag"); }
            finally { runtime.Stop(); }
            scene.MarkTransient(); Check(RuntimeAssetReferences.IsTransient(root), "Transient scene protection includes its hierarchy");
        }
        finally { BObject.Destroy(asset); BObject.Destroy(again); }
        Check(asset.isDestroyed && again.isDestroyed, "Caller can release browser references");
    }
    void Compilation()
    {
        var work=Path.Combine(output,"Work"); var empty=Path.Combine(output,"Cancelled");
        Throws<ArgumentException>(()=>PackageSourceValidation.Compile(" ",[]), "Blank working directory rejected");
        Throws<ArgumentNullException>(()=>PackageSourceValidation.Compile(work,null!), "Null dependency closure rejected");
        using(var cancel=new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(()=>PackageSourceValidation.Compile(empty,[],cancellationToken:cancel.Token), "Pre-cancelled compilation is rejected before writing files"); }
        Check(!Directory.Exists(empty), "Cancellation leaves no working directory");
        var dep=Package("dep","Fixture.Dep",false,[]); var main=Package("main","Fixture.Main",true,[dep]);
        var before=SourceHashes(); var progress=new List<(int Done,int Total,string Message)>();
        var dataPath=Application.dataPath;
        using var instance=EditorInstanceContext.Create(Path.Combine(output,"ActiveProject"));
        PackageSourceValidation.Compile(work,[main,dep],(done,total,message)=>progress.Add((done,total,message)));
        var outputs=Directory.GetFiles(Path.Combine(work,"Library"),"Fixture.*.dll",SearchOption.AllDirectories);
        Check(outputs.Select(Path.GetFileName).Distinct().Order().SequenceEqual(new[]{"Fixture.Dep.dll","Fixture.Main.Editor.dll","Fixture.Main.dll"}.Order()), "Actual compiler builds dependency Runtime, root Runtime and root Editor");
        var depAt=progress.FindIndex(p=>p.Message.StartsWith("dep (runtime)"));var mainAt=progress.FindIndex(p=>p.Message.StartsWith("main (runtime)"));var editorAt=progress.FindIndex(p=>p.Message.StartsWith("main (editor)"));
        Check(depAt>=0 && mainAt>depAt && editorAt>mainAt && progress[^1].Done==3 && progress[^1].Total==3,"Dependency order and Runtime-before-Editor order are preserved");
        Check(!File.Exists(Path.Combine(work,"Project.yaml")) && !File.Exists(Path.Combine(work,"Packages","manifest.yaml")) && Application.dataPath==dataPath && ReferenceEquals(EditorInstanceContext.current,instance),"Validation preserves the active project and editor instance");
        var instanceLogs=Path.Combine(instance.dataPath,"Logs");
        Check(!Directory.Exists(instance.scriptAssembliesPath) && (!Directory.Exists(instanceLogs) || !Directory.EnumerateFileSystemEntries(instanceLogs).Any()),"Validation does not place compiler outputs or logs in the active instance");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("Fixture.",StringComparison.Ordinal)),"Validation never loads the compiled assemblies");
        Check(before.OrderBy(p=>p.Key).SequenceEqual(SourceHashes().OrderBy(p=>p.Key)), "Source packages remain byte-for-byte unchanged");
        Throws<InvalidDataException>(()=>PackageSourceValidation.Compile(Path.Combine(output,"Missing"),[main]), "Missing mandatory dependency rejected");
        using(var cancel=new CancellationTokenSource())
        {
            var cancelledWork=Path.Combine(output,"CancelAfterDependency");
            Throws<OperationCanceledException>(()=>PackageSourceValidation.Compile(cancelledWork,[main,dep],(_,_,message)=>{if(message.StartsWith("dep (runtime)"))cancel.Cancel();},cancel.Token),"Cancellation after a completed dependency stops later work");
            Check(!Directory.GetFiles(cancelledWork,"Fixture.Main.dll",SearchOption.AllDirectories).Any(),"Cancelled compilation does not produce the dependent root");
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(main.Path)!,"Fixture.Main","Source.cs"),"this is invalid C#");
        Throws<InvalidOperationException>(()=>PackageSourceValidation.Compile(Path.Combine(output,"BadSource"),[main,dep]), "Actual compiler failure reaches the publisher");
        Check(Directory.GetFiles(Path.Combine(output,"BadSource"),"PackageCompilation.*.log",SearchOption.AllDirectories).Length>0,"Compiler diagnostics are retained in the caller's working directory");
        Check(!Directory.GetFiles(Path.Combine(output,"BadSource"),"Fixture.Main.dll",SearchOption.AllDirectories).Any(), "Failed root compilation has no successful root output");
        Dictionary<string,string> SourceHashes()=>Directory.GetFiles(Path.Combine(output,"Sources"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    }
    BPackageDefinition Package(string name,string assembly,bool editor,BPackageDefinition[] dependencies)
    {
        var root=Path.Combine(output,"Sources",name);Directory.CreateDirectory(root);
        var doc=new PackageDefinitionDocument { Id="com.fixture."+name,DisplayName=name,MinimumEngineVersion=EngineVersion.Current,Runtime=new(){Assembly=assembly,RootNamespace="Fixture"},Editor=editor?new(){Assembly=assembly+".Editor",RootNamespace="Fixture"}:null };
        foreach(var dep in dependencies)doc.Runtime.Dependencies.Add(new(){PackageId=dep.Document.Id});
        Write(doc.Runtime,false,dependencies.Select(d=>d.Document.Runtime!.Assembly).ToArray(),dependencies.Length==0?"namespace Fixture; public static class Dep { public const int Number=17; }":"namespace Fixture; public sealed class Main : BEngine.ScriptableObject { public int Number=Dep.Number; }");
        if(doc.Editor is not null)Write(doc.Editor,true,[assembly],"namespace Fixture; public static class Tool { public static Main Create()=>new Main(); }");
        var path=Path.Combine(root,"package.yaml");doc.Save(path);return new(path,doc);
        void Write(PackageAssemblyDocument item,bool editorOnly,string[] references,string source)
        {
            var folder=Path.Combine(root,item.Assembly);Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"Source.cs"),source);
            File.WriteAllText(Path.Combine(folder,item.Assembly+".asmdef.yaml"),YamlUtility.Serialize(new AssemblyDefinitionDocument { Name=item.Assembly,RootNamespace="Fixture",EditorOnly=editorOnly,References=references.ToList() }));
        }
    }
}
sealed class SharedContext() : AssemblyLoadContext(isCollectible:true), IDisposable
{
    protected override Assembly? Load(AssemblyName name) => Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==name.Name);
    public void Dispose()=>Unload();
}
