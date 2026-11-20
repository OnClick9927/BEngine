using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var test = new Probe(args[0], args[1], args[2]);
if (args[0] == "AnimationBaseline") test.WriteAnimationBaseline();
else test.Run();

sealed class Probe(string mode, string output, string legacy)
{
    readonly List<string> checks = [];
    readonly List<object> legacyInputs = [];
    int parityCases;
    long steadyStateBytes;
    void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks.Add(name);
    }
    void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, name); return; }
        throw new InvalidOperationException(name);
    }
    public void Run()
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
        DotNetPlatformServices.Initialize();
        Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Any(a => a.AssemblyName.Split(',')[0] == typeof(Probe).Assembly.GetName().Name),
            "Independent consumer has no Core private-access grant");
        switch (mode)
        {
            case "Paths": Paths(); Members(); break;
            case "Publishing": Publishing(); break;
            case "Reflection": Reflection(); break;
            case "Animation":
                if (!OperatingSystem.IsWindowsVersionAtLeast(7)) throw new PlatformNotSupportedException("Animation Editor validation requires Windows 7 or later.");
                Animation(); break;
            default: throw new ArgumentException(mode);
        }
        if (mode != "Animation")
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is
                "BEngine.Application" or "BEngine.Application.Editor" or "BEngine.EditorWorkbench.Editor" or
                "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor"),
                "Mechanism works without application, workbench or graphics providers");
        File.WriteAllText(Path.Combine(output,"Result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Checks = checks.Count, Names = checks, Mode = mode,
            PreviousBinaryComparisonCases = parityCases, SteadyStateAllocatedBytes = steadyStateBytes,
            LegacyInputs = legacyInputs
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"EDITOR_CONSUMER_CONTRACTS_OK|{mode}|checks={checks.Count}|oldBinaryCases={parityCases}");
    }
    Assembly Old(LegacyContext context, string assembly)
    {
        var path = Path.GetFullPath(Path.Combine(legacy, assembly, "release", assembly + ".dll"));
        legacyInputs.Add(new { Path = path, SHA256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) });
        return context.LoadFromAssemblyPath(path);
    }
    void Paths()
    {
        using var old = new LegacyContext();
        var type = Old(old,"BEngine").GetType("BEngine.TextSpanExtensions",true)!;
        var traversal = type.GetMethod("HasPathTraversal",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Traversal>();
        var invalid = type.GetMethod("HasInvalidPathSegment",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<InvalidSegments>();
        var contains = type.GetMethod("ContainsPathSegment",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<ContainsSegment>();
        string[] paths = ["", ".", "..", "...", "a", "a/b", "a//b", "/a", "a/", "a/../b", "a/./b",
            "a\\..\\b", "a..b", "Assets/Editor/x", "Assets/editorial/x", "Editor/x", "/Editor/x",
            "a:segment/b", "a/ /b", "a\t/b", "Assets\\EDITOR\\a", "Assets/編輯器/a"];
        foreach (var path in paths)
        foreach (var separators in new[] { "/\\", "/", "" })
        {
            Check(PathLexicalRules.HasTraversal(path, separators: separators) == traversal(path,true,separators) &&
                  PathLexicalRules.HasTraversal(path,false,separators) == traversal(path,false,separators),
                  $"Traversal matches previous binary: '{path}' separators='{separators}'"); parityCases++;
            foreach (var strict in new[] {false,true})
            {
                Check(PathLexicalRules.HasInvalidSegments(path,strict,strict,separators) == invalid(path,strict,strict,separators),
                    $"Invalid segments match previous binary: '{path}' strict={strict} separators='{separators}'"); parityCases++;
            }
        }
        foreach (var path in paths)
        foreach (var value in new[] {"Editor","editor","", "b"})
        foreach (var skip in new[] {false,true})
        {
            Check(PathLexicalRules.ContainsSegment(path,value,StringComparison.OrdinalIgnoreCase,skip) ==
                contains(path,value,StringComparison.OrdinalIgnoreCase,skip),$"Segment match preserves previous grammar: '{path}', '{value}', skip={skip}"); parityCases++;
        }
        Check(!PathLexicalRules.ContainsSegment("Assets/Editorial/a","Editor") &&
              PathLexicalRules.ContainsSegment("Assets/Editor/a","Editor",skipFirst:true), "Editor exclusion matches whole segments only");
        Check(!PathLexicalRules.HasTraversal("a..b") && PathLexicalRules.HasTraversal("a/../b"), "Dots in a filename differ from traversal");
        Throws<ArgumentNullException>(() => PathLexicalRules.HasTraversal("a",separators:null!),"Null traversal delimiters rejected");
        Throws<ArgumentNullException>(() => PathLexicalRules.HasInvalidSegments("a",separators:null!),"Null invalid-segment delimiters rejected");
        Action query=()=>_ = QueryPaths();
        _=Measure(query);_=Measure(query);
        var bytes=Measure(query);steadyStateBytes+=bytes;
        Check(bytes==0,$"Repeated lexical queries allocate zero managed bytes ({bytes})");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool QueryPaths() => PathLexicalRules.ContainsSegment("Assets/Editor/item","Editor",skipFirst:true) &&
        PathLexicalRules.HasTraversal("a/../b") && !PathLexicalRules.HasInvalidSegments("Assets/example.asset");
    void Members()
    {
        RuntimeTypeCache.RegisterAssemblies([typeof(Probe).Assembly]);
        var expected = ComponentFieldSerializer.GetInspectorMembers(typeof(ConsumerComponent)).Select(m=>m.Name).ToArray();
        var serial = ComponentFieldSerializer.GetSerializableMembers(typeof(ConsumerComponent));
        var inspector = ComponentFieldSerializer.GetInspectorMembers(typeof(ConsumerComponent));
        Check(serial is not MemberInfo[] && inspector is not MemberInfo[],"Public member queries never expose metadata arrays");
        Check(serial.Count>0 && inspector.Count>0 && ((IList<MemberInfo>)serial).IsReadOnly && ((IList<MemberInfo>)inspector).IsReadOnly,
            "Both serialization and inspector views are immutable");
        Throws<NotSupportedException>(()=>((IList<MemberInfo>)serial)[0]=typeof(ConsumerComponent).GetField(nameof(ConsumerComponent.Number))!,"Serialization view rejects mutation");
        Throws<NotSupportedException>(()=>((IList<MemberInfo>)inspector).Clear(),"Inspector view rejects clearing");
        Check(expected.SequenceEqual(ComponentFieldSerializer.GetInspectorMembers(typeof(ConsumerComponent)).Select(m=>m.Name)),"Failed consumer mutations preserve the discovery cache");
        Check(ReferenceEquals(serial,ComponentFieldSerializer.GetSerializableMembers(typeof(ConsumerComponent))) &&
              ReferenceEquals(inspector,ComponentFieldSerializer.GetInspectorMembers(typeof(ConsumerComponent))),"Queries reuse immutable views within a generation");
        Throws<ArgumentNullException>(()=>ComponentFieldSerializer.GetInspectorMembers(null!),"Null inspector type rejected");
        Throws<ArgumentNullException>(()=>ComponentFieldSerializer.GetSerializableMembers(null!),"Null serialized type rejected");
        Action query=QueryMembers;
        _=Measure(query);_=Measure(query);
        var bytes=Measure(query);steadyStateBytes+=bytes;
        Check(bytes==0,$"Repeated member queries allocate zero managed bytes ({bytes})");
        var weak=RetiredMemberView();
        for(var i=0;i<20 && weak.IsAlive;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Thread.Sleep(10);}
        Check(!weak.IsAlive,"Immutable member views do not retain a retired collectible assembly");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Measure(Action query)
    {
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10000;i++) query();
        return GC.GetAllocatedBytesForCurrentThread()-before;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void QueryMembers()
    {
        _=ComponentFieldSerializer.GetSerializableMembers(typeof(ConsumerComponent));
        _=ComponentFieldSerializer.GetInspectorMembers(typeof(ConsumerComponent));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RetiredMemberView()
    {
        var type=DynamicComponent("RetiredView"+Guid.NewGuid().ToString("N"));
        var assembly=type.Assembly;var weak=new WeakReference(assembly);
        RuntimeTypeCache.RegisterAssemblies([assembly]);
        _=ComponentFieldSerializer.GetSerializableMembers(type);
        _=ComponentFieldSerializer.GetInspectorMembers(type);
        RuntimeTypeCache.UnregisterAssemblies([assembly]);
        AssemblyLoadContext.GetLoadContext(assembly)!.Unload();
        return weak;
    }
    static Type DynamicComponent(string name)
    {
        using var definition=Mono.Cecil.AssemblyDefinition.CreateAssembly(
            new Mono.Cecil.AssemblyNameDefinition(name,new Version(1,0)),name,Mono.Cecil.ModuleKind.Dll);
        var module=definition.MainModule;
        var type=new Mono.Cecil.TypeDefinition("EditorConsumerFixtures","Component",Mono.Cecil.TypeAttributes.Public,module.ImportReference(typeof(MonoBehaviour)));
        type.Fields.Add(new Mono.Cecil.FieldDefinition("Number",Mono.Cecil.FieldAttributes.Public,module.ImportReference(typeof(Fix64))));
        module.Types.Add(type);
        using var image=new MemoryStream();definition.Write(image);image.Position=0;
        return new LegacyContext().LoadFromStream(image).GetType("EditorConsumerFixtures.Component",true)!;
    }
    void Publishing()
    {
        using var old=new LegacyContext();
        var legacyEditor=Old(old,"BEngine.Player.Editor");
        var compress=legacyEditor.GetType("BEngine.Editor.Build.PlayerResourceArchiveCompression",true)!
            .GetMethod("IsEnabled",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<Compression>();
        foreach(var platform in Enum.GetValues<BuildTargetPlatform>().Cast<BuildTargetPlatform?>().Append(null))
        foreach(var requested in new[]{false,true})
        {
            Check(PlatformContentBuildPolicy.CompressBuiltInArchive(platform,requested)==compress(platform,requested),
                $"Platform preserves native archive capability: {platform} requested={requested}");parityCases++;
        }
        Check(!PlatformContentBuildPolicy.CompressBuiltInArchive(BuildTargetPlatform.Web,true),"Web cannot request unsupported runtime Brotli decoding");
        Check(typeof(PlatformContentBuildPolicy).Assembly.GetName().Name=="BEngine.Platform.Build","Native content-codec policy belongs to Platform");
        Check(typeof(PlayerOutputValidation).Assembly.GetName().Name=="BEngine.Core.Editor","Common publication validation belongs to Core Editor");
        var validate=legacyEditor.GetType("BEngine.Editor.Build.PlayerBuildLayout",true)!
            .GetMethod("ValidateLowercaseOutputTree",BindingFlags.NonPublic|BindingFlags.Static,null,
                [typeof(string),typeof(FileUtility),typeof(PathUtility)],null)!.CreateDelegate<OutputValidation>();
        var files=FileUtility.Current;var paths=PathUtility.Current;
        foreach(var item in new[]{
            ("lower", "framework.bin"), ("upper","Framework.bin"), ("nested","files/Upper.bin"),
            ("data","game_data/StreamingAssets/CustomCase/Logo.PNG"),
            ("notdata","files/StreamingAssets/Logo.PNG"),
            ("wrongrawcase","game_data/streamingassets/Logo.PNG"),
            ("deepdata","nested/game_data/StreamingAssets/Logo.PNG"),
            ("unicode","files/圖示.bin"), ("nonasciiuppercase","files/Ä.bin")})
        {
            var root=Path.Combine(output,"layout",item.Item1);var target=Path.Combine(root,item.Item2);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllBytes(target,[1,2,3]);
            var a=Failure(()=>validate(root,files,paths));var b=Failure(()=>PlayerOutputValidation.ValidateLowercaseTree(root,files,paths));
            Check(a==b,$"Published path grammar preserves previous output: {item.Item1} ({b??"valid"})");parityCases++;
        }
        var missing=Path.Combine(output,"absent");
        Check(Failure(()=>validate(missing,files,paths))==Failure(()=>PlayerOutputValidation.ValidateLowercaseTree(missing,files,paths)),"Missing output preserves previous diagnostic");parityCases++;
        Throws<ArgumentException>(()=>PlayerOutputValidation.ValidateLowercaseTree(" ",files,paths),"Empty publication root rejected");
        Throws<ArgumentNullException>(()=>PlayerOutputValidation.ValidateLowercaseTree(output,null!,paths),"Null publication filesystem rejected");
        Throws<ArgumentNullException>(()=>PlayerOutputValidation.ValidateLowercaseTree(output,files,null!),"Null publication path service rejected");
        var versions=Old(old,"BEngine.Player").GetType("BEngine.Player.ContentVersionLabel",true)!;
        var normalize=versions.GetMethod("NormalizeHotResourceVersion")!.CreateDelegate<Func<string?,string>>();
        var compare=versions.GetMethod("CompareHotResourceVersions")!.CreateDelegate<Func<string,string,int>>();
        foreach(var value in new[]{"v1","v9","v10","v123456789012345678901234567890","v01","v0","V1"," v1","v1/../v2","con","v1."})
        {
            Check(Failure(()=>normalize(value))==Failure(()=>BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(value)),$"Release protocol keeps previous validation: {value}");parityCases++;
        }
        foreach(var pair in new[]{("v1","v2"),("v9","v10"),("v99","v100"),("v10","v10"),("v12345678901234567890","v12345678901234567891")})
        {
            Check(compare(pair.Item1,pair.Item2)==BEngine.Player.ContentVersionLabel.CompareHotResourceVersions(pair.Item1,pair.Item2),$"Release protocol keeps previous ordering: {pair}");parityCases++;
        }
    }
    static string? Failure(Action action)
    {
        try{action();return null;}catch(Exception error){return error.GetType().FullName+":"+error.Message;}
    }
    void Reflection()
    {
        Check(typeof(EditorExtensionLifecycle).Assembly.GetName().Name=="BEngine.Core.Editor","Reflection synchronization is owned by Core Editor");
        var warmups=0;TypeCache.RegisterWarmup("EditorConsumerContracts",()=>warmups++);
        var original=DynamicComponent("EditScope"+Guid.NewGuid().ToString("N"));
        var injected=DynamicComponent("PlayScope"+Guid.NewGuid().ToString("N"));
        RuntimeTypeCache.RegisterAssemblies([original.Assembly]);
        EditorExtensionLifecycle.SynchronizeReflection();
        Check(ReferenceEquals(RuntimeTypeCache.FindType(original.FullName!),original),"Edit-time identity is visible before injection");
        using(var preference=RuntimeTypeCache.PreferAssemblies([injected.Assembly]))
        {
            EditorExtensionLifecycle.SynchronizeReflection();
            Check(ReferenceEquals(RuntimeTypeCache.FindType(original.FullName!),injected),"Preferred domain wins the full-name collision");
            Check(TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Contains(injected),"Editor queries include the active domain");
            Check(warmups==2,"Registered editor warmups run after the preferred catalog is installed");
            RuntimeTypeCache.UnregisterAssemblies([injected.Assembly]);
        }
        EditorExtensionLifecycle.SynchronizeReflection();
        Check(ReferenceEquals(RuntimeTypeCache.FindType(original.FullName!),original),"Retirement restores edit-time identity");
        Check(!TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Contains(injected),"Editor queries drop retired domain types");
        Check(warmups==3,"Retirement synchronizes registered reflected extensions");
        var failed=DynamicComponent("FailedScope"+Guid.NewGuid().ToString("N"));
        IDisposable? failedPreference=null;
        try
        {
            failedPreference=RuntimeTypeCache.PreferAssemblies([failed.Assembly]);
            EditorExtensionLifecycle.SynchronizeReflection();
            throw new InvalidDataException("activation failed");
        }
        catch(InvalidDataException)
        {
            failedPreference?.Dispose();RuntimeTypeCache.UnregisterAssemblies([failed.Assembly]);
            EditorExtensionLifecycle.SynchronizeReflection();
        }
        Check(ReferenceEquals(RuntimeTypeCache.FindType(original.FullName!),original) && !TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Contains(failed),
            "Failed preparation cleanup restores the reflected editor catalog");
        TypeCache.UnregisterWarmups(typeof(Probe).Assembly);
        var calls=warmups;EditorExtensionLifecycle.SynchronizeReflection();
        Check(warmups==calls,"Retired provider callbacks are not called again");
        RuntimeTypeCache.UnregisterAssemblies([original.Assembly]);
        EditorExtensionLifecycle.SynchronizeReflection();
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]
    void Animation()
    {
        DotNetEditorPlatform.Initialize();
        using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
            .AddSingleton<IEditorUtilityPlatform, NoDialogs>().BuildServiceProvider();
        using var scope=EditorServiceScope.Attach(services);
        RuntimeTypeCache.RegisterAssemblies([typeof(Probe).Assembly]);
        using var scene=new Scene("animation consumer");var root=scene.CreateGameObject("Root");
        var component=root.AddComponent<ConsumerComponent>();
        var child=scene.CreateGameObject("Child");child.transform.SetParent(root.transform);
        child.AddComponent<ConsumerComponent>();
        // Old providers may rely on the old Core's internal-access metadata. The
        // script executes that graph in its own process, never in the new Core.
        using var baseline=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"Baseline","AnimationBaseline.json")));
        var previous=baseline.RootElement.GetProperty("Properties").EnumerateArray().Select(p=>p.GetString()!).ToArray();
        legacyInputs.Add(baseline.RootElement.GetProperty("Inputs").Clone());
        var current=AnimationTimelineEditing.GetProperties(root).Select(Describe).ToArray();
        Check(previous.SequenceEqual(current),"Animation discovers exactly the previous ordered root/child properties");parityCases++;
        Check(current.Any(s=>s.EndsWith("|Tint.r")) && current.Any(s=>s.EndsWith("|Offset.x")),"Color and vector members expose their animated channels");
        Check(!current.Any(s=>s.EndsWith("|Hidden")),"HideInInspector retains the previous animation exclusion");
        var clip=new AnimationClip();var property=AnimationTimelineEditing.GetProperties(root).First(p=>p.Component==component && p.PropertyName==nameof(ConsumerComponent.Number));
        var binding=AnimationTimelineEditing.AddProperty(clip,property,1);
        Check(binding.propertyName==nameof(ConsumerComponent.Number) && clip.bindings.Count==1,"Discovered metadata still creates runtime animation bindings");
        BObject.DestroyImmediate(clip);
    }
    public void WriteAnimationBaseline()
    {
        Directory.CreateDirectory(output);
        DotNetPlatformServices.Initialize();
        using var scene=new Scene("animation consumer");var root=scene.CreateGameObject("Root");
        root.AddComponent<ConsumerComponent>();
        var child=scene.CreateGameObject("Child");child.transform.SetParent(root.transform);
        child.AddComponent<ConsumerComponent>();
        using var old=new LegacyContext();
        var method=Old(old,"BEngine.Animation.Editor").GetType("BEngine.Editor.AnimationTimelineEditing",true)!.GetMethod("GetProperties")!;
        var properties=((IEnumerable)method.Invoke(null,[root])!).Cast<object>().Select(Describe).ToArray();
        var core=typeof(BObject).Assembly.Location;
        legacyInputs.Add(new { Path=core, SHA256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(core))) });
        File.WriteAllText(Path.Combine(output,"AnimationBaseline.json"),JsonSerializer.Serialize(new {
            Properties=properties, Inputs=legacyInputs, ProcessId=Environment.ProcessId
        },new JsonSerializerOptions { WriteIndented=true }));
    }
    static string Describe(object property)
    {
        var type=property.GetType();
        return type.GetProperty("RelativePath")!.GetValue(property)+"|"+type.GetProperty("ComponentType")!.GetValue(property)+"|"+type.GetProperty("PropertyName")!.GetValue(property);
    }
    delegate bool Traversal(ReadOnlySpan<char> path,bool includeCurrent,string separators);
    delegate bool InvalidSegments(ReadOnlySpan<char> path,bool whitespace,bool colon,string separators);
    delegate bool ContainsSegment(ReadOnlySpan<char> path,ReadOnlySpan<char> segment,StringComparison comparison,bool skipFirst);
    delegate bool Compression(BuildTargetPlatform? platform,bool requested);
    delegate void OutputValidation(string root,FileUtility files,PathUtility paths);
}
sealed class LegacyContext() : AssemblyLoadContext(isCollectible:true),IDisposable
{
    protected override Assembly? Load(AssemblyName name) => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==name.Name);
    public void Dispose()=>Unload();
}
public sealed class ConsumerComponent:MonoBehaviour
{
    public Fix64 Number=3;
    public Vector2 Offset=new(1,2);
    public Color Tint=Color.white;
    [HideInInspector] public Fix64 Hidden=4;
}
public sealed class NoDialogs:IEditorUtilityPlatform
{
    public int DisplayDialog(string title,string message,IReadOnlyList<string> buttons)=>throw new NotSupportedException();
    public string OpenFilePanel(string title,string directory,string filter)=>throw new NotSupportedException();
    public string OpenFolderPanel(string title,string folder,string defaultName)=>throw new NotSupportedException();
    public void OpenWithDefaultApp(string target)=>throw new NotSupportedException();
    public void ShowProgress(EditorProgressInfo progress,Action? requestCancellation)=>throw new NotSupportedException();
    public void ClearProgress()=>throw new NotSupportedException();
}
