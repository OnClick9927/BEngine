using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.ProjectSystem.Editor;

public static class ShaderWorkflow
{
    static string Output=>Environment.GetEnvironmentVariable("BENGINE_SHADER_RESULT")??"";
    static string Stage=>Path.Combine(Output,"Stage.txt");
    static string Details=>Path.Combine(Output,"Checks.txt");
    static bool Finished;
    static double Started;
    [InitializeOnLoadMethod] public static void Bind()
    {
        if(Output.Length==0)return;
        Started=EditorApplication.timeSinceStartup;
        File.AppendAllText(Path.Combine(Output,"Events.txt"),"init:"+ReloadMarker.Value+"\n");
        AssemblyReloadEvents.beforeAssemblyReload+=()=>File.AppendAllText(Path.Combine(Output,"Events.txt"),"before:"+ReloadMarker.Value+"\n");
        AssemblyReloadEvents.afterAssemblyReload+=()=>File.AppendAllText(Path.Combine(Output,"Events.txt"),"after:"+ReloadMarker.Value+"\n");
        EditorApplication.update+=Tick;
    }
    static void Check(bool condition,string name)
    {if(!condition)throw new Exception(name);File.AppendAllText(Details,name+"\n");}
    static object Host()=>typeof(EditorApplication).Assembly.GetType("BEngine.Editor.EditorBridge",true)
        .GetProperty("Host",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
    static ProjectShaderCompilationSession Session(object host)=>(ProjectShaderCompilationSession)host.GetType()
        .GetField("_shaderCompiler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
    static void Tick()
    {
        if(Finished||EditorApplication.isCompiling)return;
        try
        {
            if(EditorApplication.timeSinceStartup-Started>120)throw new Exception("Actual shader workflow timed out");
            var phase=File.Exists(Stage)?File.ReadAllText(Stage):"initial";
            var workspace=BEngine.ProjectSystem.ProjectWorkspace.Open(EditorApplication.projectPath);
            var artifact=ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg");
            if(phase=="initial")
            {
                Check(artifact is not null,"Actual startup compiles and applies a Graphics shader through the Core session");
                Check(BitConverter.ToUInt32(File.ReadAllBytes(artifact),0)==0x07230203,"The Windows native backend produced a real SPIR-V artifact");
                Check(File.ReadAllLines(Path.Combine(Output,"Events.txt")).SequenceEqual(new[]{"init:1","after:1"}),"The actual host announces initial readiness after extension initialization");
                Check(Application.isEditor&&!Application.isPlaying,"Compilation preserves the editor application state");
                var scene=EditorSceneManager.GetActiveScene();
                Check(scene.Id==new Guid("173f1a97-857b-4fa6-bc08-1a44edc35485"),"Startup keeps the existing scene identity");
                var selected=scene.CreateGameObject("Shader reload selection");Selection.activeGameObject=selected;
                File.WriteAllText(Path.Combine(Output,"Selected.txt"),selected.Id.ToString());
                File.WriteAllText(Path.Combine(Output,"Artifact.txt"),artifact);
                File.WriteAllText(Path.Combine(Output,"Session.txt"),RuntimeHelpers.GetHashCode(Session(Host())).ToString());
                File.WriteAllText(Stage,"shader");
                File.WriteAllText(Path.Combine(EditorApplication.projectPath,"Assets/Sample.frag.cg"),"#pragma stage fragment\nfloat4 main() : SV_Target { return float4(0,1,0,1); }\n");
                return;
            }
            if(phase=="shader"&&artifact is not null&&artifact!=File.ReadAllText(Path.Combine(Output,"Artifact.txt")))
            {
                Check(BitConverter.ToUInt32(File.ReadAllBytes(artifact),0)==0x07230203,"Source monitoring and background compilation publish another native shader artifact");
                var host=Host();var session=Session(host);
                Check(session.GetType().Assembly==typeof(ProjectScriptHostSession).Assembly,"The actual workbench uses the Core-owned shader session");
                var stale=session.PrepareCompilation(new[]{"Assets/Sample.frag.cg"}).Build();
                host.GetType().GetMethod("CancelShaderCompilation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(host,null);
                host.GetType().GetMethod("ApplyShaderCompilation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(host,new object[]{stale});
                Check(!session.TryApply(stale),"Actual host cancellation invalidates Core-owned completed shader results");
                Check(ProjectShaderCompiler.ResolveCurrentArtifact(workspace,"Assets/Sample.frag.cg")==artifact,"A stale actual-host apply preserves the published pointer");
                State();
                File.WriteAllText(Path.Combine(Output,"Artifact.txt"),artifact);
                File.WriteAllText(Stage,"runtime");
                File.WriteAllText(Path.Combine(EditorApplication.projectPath,"Assets/Runtime/Runtime.cs"),"public static class ReloadMarker { public static int Value => 2; }");
                CompilationPipeline.RequestScriptCompilation();return;
            }
            if(phase=="runtime"&&ReloadMarker.Value==2)
            {
                var events=File.ReadAllLines(Path.Combine(Output,"Events.txt"));
                Check(events.Contains("before:1")&&events.Contains("init:2")&&events.Contains("after:2"),"Runtime plus Editor replacement preserves lifecycle notifications");
                Check(Array.IndexOf(events,"before:1")<Array.IndexOf(events,"init:2")&&Array.IndexOf(events,"init:2")<Array.IndexOf(events,"after:2"),"Reload notification order remains before, initialization, after");
                Check(RuntimeHelpers.GetHashCode(Session(Host())).ToString()==File.ReadAllText(Path.Combine(Output,"Session.txt")),"Project code replacement preserves the host's shader session ownership");
                Check(artifact==File.ReadAllText(Path.Combine(Output,"Artifact.txt")),"Project script replacement preserves the published shader artifact");
                State();Complete(true,"");
            }
        }
        catch(Exception failure){Complete(false,failure.ToString());}
    }
    static void State()
    {
        var id=Guid.Parse(File.ReadAllText(Path.Combine(Output,"Selected.txt")));
        Check(Selection.activeGameObject?.Id==id,"Compilation preserves scene selection");
        Check(EditorSceneManager.GetActiveScene().gameObjects.Any(o=>o.Id==id),"Compilation preserves unsaved scene objects");
        Check(Application.isEditor&&!Application.isPlaying&&!Time.isFrameActive,"Compilation finishes outside a simulation frame");
    }
    static void Complete(bool passed,string error)
    {
        Finished=true;var details=File.Exists(Details)?File.ReadAllLines(Details):Array.Empty<string>();
        File.WriteAllText(Path.Combine(Output,"Result.json"),JsonSerializer.Serialize(new{Passed=passed,Checks=details.Length,Details=details,Error=error}));
        EditorApplication.Exit(passed?0:1);
    }
}
