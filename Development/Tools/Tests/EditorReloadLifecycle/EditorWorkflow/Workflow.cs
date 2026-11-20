using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using BEngine;
using BEngine.Editor;

public static class ReloadWorkflow
{
    static string Output => Environment.GetEnvironmentVariable("BENGINE_RELOAD_RESULT") ?? "";
    static string StageFile => Path.Combine(Output,"Stage.txt");
    static string EventsFile => Path.Combine(Output,"Events.txt");
    static string DetailsFile => Path.Combine(Output,"Checks.txt");
    static bool Finished;
    static double Started;

    [InitializeOnLoadMethod]
    public static void Bind()
    {
        if(Output.Length==0)return;
        Started=EditorApplication.timeSinceStartup;
        Record("init:"+ReloadMarker.Value);
        AssemblyReloadEvents.beforeAssemblyReload+=()=>Record("before:"+ReloadMarker.Value);
        AssemblyReloadEvents.afterAssemblyReload+=()=>Record("after:"+ReloadMarker.Value);
        EditorApplication.update+=Tick;
    }
    static void Record(string text)=>File.AppendAllText(EventsFile,text+"\n");
    static void Check(bool value,string name)
    {
        if(!value)throw new Exception(name);
        File.AppendAllText(DetailsFile,name+"\n");
    }
    static void Tick()
    {
        if(Finished||EditorApplication.isCompiling)return;
        try
        {
            if(EditorApplication.timeSinceStartup-Started>120)throw new Exception("Reload workflow timed out");
            var phase=File.Exists(StageFile)?File.ReadAllText(StageFile):"initial";
            var events=File.ReadAllLines(EventsFile);
            if(phase=="initial")
            {
                Check(events.SequenceEqual(new[]{"init:1","after:1"}),"Actual startup initializes extensions before announcing readiness, without a reload-before event");
                Check(Application.isEditor&&!Application.isPlaying,"Startup retains editor application state");
                var scene=EditorSceneManager.GetActiveScene();
                Check(scene.Id==new Guid("173f1a97-857b-4fa6-bc08-1a44edc35485"),"Startup retains the source scene GUID");
                var selected=scene.CreateGameObject("Preserved across code reload");
                Selection.activeGameObject=selected;
                File.WriteAllText(Path.Combine(Output,"Selected.txt"),selected.Id.ToString());
                File.WriteAllText(StageFile,"runtime");
                File.WriteAllText(Path.Combine(EditorApplication.projectPath,"Assets/Runtime/Runtime.cs"),"public static class ReloadMarker { public static int Value => 2; }");
                CompilationPipeline.RequestScriptCompilation();
                return;
            }
            if(phase=="runtime"&&ReloadMarker.Value==2&&events.Contains("after:2"))
            {
                Check(events.Contains("before:1"),"The actual host announces replacement while the previous graph is active");
                Check(events.Contains("init:2"),"The new Editor graph initializes against the matching Runtime graph");
                Check(Array.IndexOf(events,"before:1")<Array.IndexOf(events,"init:2")&&Array.IndexOf(events,"init:2")<Array.IndexOf(events,"after:2"),"Actual Runtime reload preserves before, initialization and after order");
                CheckState();
                File.WriteAllText(StageFile,"editor");
                File.AppendAllText(Path.Combine(EditorApplication.projectPath,"Assets/Editor/Workflow.cs"),"\npublic static class EditorRevisionTwo { public static int Value => 2; }\n");
                CompilationPipeline.RequestScriptCompilation();
                return;
            }
            if(phase=="editor"&&RuntimeTypeCache.ResolveSerializedType("EditorRevisionTwo") is not null)
            {
                Check(events.Count(s=>s=="init:2")>=2,"An Editor-only change initializes the replacement Editor assembly");
                Check(events.Contains("before:2")&&events.Count(s=>s=="after:2")>=2,"Editor-only replacement also pairs lifecycle notifications");
                Check(events.Count(s=>s.StartsWith("init:"))==events.Count(s=>s.StartsWith("after:")),"Actual reloads produce one readiness event per initialized graph");
                Check(events.Count(s=>s.StartsWith("before:"))+1==events.Count(s=>s.StartsWith("after:")),"Only initial startup omits the matching before event");
                CheckState();
                CheckEarlyFailure();
                using(var next=AssemblyReloadEvents.BeginInitialization()) { }
                Check(true,"The actual host releases lifecycle ownership after replacement");
                Complete(true,"");
            }
        }
        catch(Exception failure){Complete(false,failure.ToString());}
    }
    static void CheckState()
    {
        var id=Guid.Parse(File.ReadAllText(Path.Combine(Output,"Selected.txt")));
        Check(Selection.activeGameObject?.Id==id,"Reload preserves the selected scene object");
        Check(EditorSceneManager.GetActiveScene().gameObjects.Any(o=>o.Id==id),"Reload preserves unsaved scene objects");
        Check(Application.isEditor&&!Application.isPlaying&&!Time.isFrameActive,"Reload returns to a settled editor state outside simulation");
    }
    static void CheckEarlyFailure()
    {
        // BCL-owned observers survive retirement of this fixture's project assembly.
        // Invoke the real synchronous host path while another compilation owns Core.
        using var before=new ManualResetEventSlim();using var after=new ManualResetEventSlim();
        AssemblyReloadEvents.beforeAssemblyReload+=before.Set;
        AssemblyReloadEvents.afterAssemblyReload+=after.Set;
        try
        {
            using(var compilation=CompilationPipeline.BeginCompilation(new object()))
            {
                var host=typeof(EditorApplication).Assembly.GetType("BEngine.Editor.EditorBridge",true)
                    .GetProperty("Host",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                var rejected=false;
                try { host.GetType().GetMethod("CompileScripts",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(host,null); }
                catch(TargetInvocationException failure) when(failure.InnerException is InvalidOperationException){rejected=true;}
                Check(rejected,"The actual synchronous host rejects overlapping compilation after reload setup");
                Check(before.IsSet&&after.IsSet,"Failure before compilation setup still pairs actual host reload notifications");
                Check(CompilationPipeline.isCompiling,"Rejected host setup cannot take the caller's compilation transaction");
            }
            Check(!CompilationPipeline.isCompiling,"The caller can complete its transaction after rejected host setup");
        }
        finally
        {
            AssemblyReloadEvents.beforeAssemblyReload-=before.Set;
            AssemblyReloadEvents.afterAssemblyReload-=after.Set;
        }
    }
    static void Complete(bool passed,string error)
    {
        Finished=true;
        var details=File.Exists(DetailsFile)?File.ReadAllLines(DetailsFile):Array.Empty<string>();
        File.WriteAllText(Path.Combine(Output,"Result.json"),JsonSerializer.Serialize(new{Passed=passed,Checks=details.Length,Details=details,Error=error}));
        EditorApplication.Exit(passed?0:1);
    }
}
