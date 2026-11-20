using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.Editor;

// Drives actual workbench updates and commands; production services are not replaced.
public static class HostRuntimeWorkflow
{
    static readonly List<string> Checks = new();
    static string Output => Environment.GetEnvironmentVariable("BENGINE_HOST_RUNTIME_RESULT") ?? "";
    static int Step, Settled;
    static bool EnteredPlay, EnteredEdit;
    static double Started;
    static Scene Source;
    static HostRuntimeCounter Counter;
    static long PausedFrames;
    static Fix64 PausedTime;
    [InitializeOnLoadMethod]
    public static void Bind()
    {
        if (Output.Length == 0) return;
        Started = EditorApplication.timeSinceStartup;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EnteredPlay = true;
            if (state == PlayModeStateChange.EnteredEditMode) EnteredEdit = true;
        };
        EditorApplication.update += Tick;
    }
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Checks.Add(name); }
    static object Host => typeof(EditorApplication).Assembly.GetType("BEngine.Editor.EditorBridge", true)
        .GetProperty("Host", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static void Tick()
    {
        if (Step == 99 || EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup - Started > 120) throw new Exception("Host workflow timed out at " + Step);
            if (Step == 0)
            {
                // A hidden verification window can lose focus on its first update;
                // the real host then resets held input. Let that bootstrap settle.
                if (Settled++ < 2) return;
                Source = EditorSceneManager.GetActiveScene();
                Check(Application.isEditor && !Application.isPlaying && !EditorApplication.isPlaying,
                    "Actual bootstrap establishes editor application state");
                Check(Application.dataPath == Path.Combine(EditorApplication.projectPath, "Assets"),
                    "Actual bootstrap retains the project assets address");
                Check(Source.Id == new Guid("173f1a97-857b-4fa6-bc08-1a44edc35485") && Source.isLoaded,
                    "Actual bootstrap preserves the startup scene GUID and state");
                Input.SetKeyState(KeyCode.A, true);
                Check(Input.GetKeyDown(KeyCode.A), "The actual input provider exposes a newly pressed key");
                Step = 1; return;
            }
            if (Step == 1)
            {
                Check(!Input.GetKeyDown(KeyCode.A) && Input.GetKey(KeyCode.A),
                    "A normal native update clears input edges while retaining held state");
                Input.SetKeyState(KeyCode.A, false); Input.SetKeyState(KeyCode.B, true);
                var frames = Time.frameCount;
                var update = Host.GetType().GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                var rejected = false;
                try { update.Invoke(Host, new object[] { double.NaN }); }
                catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { rejected = true; }
                Check(rejected, "The actual update rejects invalid elapsed time before normal input completion");
                Check(!Input.GetKeyDown(KeyCode.B) && Input.GetKey(KeyCode.B) && !Input.GetKeyUp(KeyCode.A),
                    "The actual exceptional update still completes the input frame");
                Check(Time.frameCount == frames && !Time.isFrameActive,
                    "The rejected update neither advances nor leaves a simulation frame open");
                Input.SetKeyState(KeyCode.B, false);
                Settled = 0;
                Step = 2; EditorApplication.isPlaying = true; return;
            }
            if (Step == 2)
            {
                if (!EnteredPlay || Settled++ == 0) return;
                Check(EditorApplication.isPlaying && Application.isPlaying && Application.isEditor,
                    "Actual Play Mode publishes playing state while retaining editor identity");
                var playing = EditorSceneManager.GetActiveScene();
                Check(playing != Source && playing.Id == Source.Id && RuntimeAssetReferences.IsTransient(playing),
                    "Actual Play Mode retains isolated scene identity and lifetime");
                EditorApplication.isPaused = true;
                Check(EditorApplication.isPaused && Application.isPlaying, "Pause retains the application's playing state");
                Counter = playing.CreateGameObject("step counter").AddComponent<HostRuntimeCounter>();
                Check(Counter.Awakes == 1 && Counter.Updates == 0, "Paused runtime attachment initializes without ticking");
                var frames = Time.frameCount;
                Input.SetKeyState(KeyCode.C, true); EditorApplication.Step();
                Check(Counter.Updates == 1 && Counter.SawActiveFrame, "The real Step command executes one runtime update inside its frame");
                Check(Time.frameCount == frames + 1 && !Time.isFrameActive, "The real Step command advances exactly one clock frame");
                Check(!Input.GetKeyDown(KeyCode.C) && Input.GetKey(KeyCode.C), "The real Step command completes input immediately");
                PausedFrames = Time.frameCount; PausedTime = Time.time; Step = 3; return;
            }
            if (Step == 3)
            {
                Check(Counter.Updates == 1 && Time.frameCount == PausedFrames && Time.time == PausedTime,
                    "Subsequent paused native updates do not advance simulation");
                EditorApplication.Step();
                Check(Counter.Updates == 2 && Time.frameCount == PausedFrames + 1,
                    "A second Step command advances once without losing dispatch identity");
                EditorApplication.isPaused = false; Step = 4; return;
            }
            if (Step == 4)
            {
                Check(Counter.Updates > 2 && Time.frameCount > PausedFrames + 1,
                    "Unpausing resumes the actual scene runtime and clock");
                Check(!Time.isFrameActive, "Editor update callbacks run outside the scene simulation frame");
                EditorApplication.isPaused = true; EditorApplication.isPlaying = false; Step = 5; return;
            }
            if (Step == 5)
            {
                if (!EnteredEdit || EditorApplication.isPlayingOrWillChangePlaymode) return;
                Check(Application.isEditor && !Application.isPlaying && !EditorApplication.isPaused,
                    "Actual shutdown returns application and pause state to edit mode");
                Check(EditorSceneManager.GetActiveScene() == Source && Source.isCreated && !RuntimeAssetReferences.IsTransient(Source),
                    "Actual shutdown restores the original persistent edit scene");
                Check(Counter.Destroys == 1 && Counter.WasPlayingWhenDestroyed,
                    "Runtime destruction occurs once before edit-mode state is published");
                Check(!Time.isFrameActive, "Actual shutdown closes every simulation frame before returning");
                EnteredPlay = false; EnteredEdit = false; Settled = 0;
                Step = 6; EditorApplication.isPlaying = true; return;
            }
            if (Step == 6)
            {
                if (!EnteredPlay || Settled++ == 0) return;
                Check(Application.isPlaying && EditorSceneManager.GetActiveScene() != Source,
                    "A second Play Mode run activates a fresh runtime session");
                Check(EditorSceneManager.GetActiveScene().gameObjects.Count == 0 && Counter.Destroys == 1,
                    "The second run contains no objects or callbacks from the previous runtime");
                EditorApplication.isPlaying = false; Step = 7; return;
            }
            if (Step == 7)
            {
                if (!EnteredEdit || EditorApplication.isPlayingOrWillChangePlaymode) return;
                Check(!Application.isPlaying && Application.isEditor && EditorSceneManager.GetActiveScene() == Source,
                    "Repeated transitions preserve editor state and source scene identity");
                Check(Application.dataPath == Path.Combine(EditorApplication.projectPath, "Assets"),
                    "Repeated transitions preserve the host's assets address");
                Complete(true, "");
            }
        }
        catch (Exception error) { Complete(false, error.ToString()); }
    }
    static void Complete(bool passed, string error)
    {
        Step = 99;
        File.WriteAllText(Path.Combine(Output, "Result.json"), JsonSerializer.Serialize(new { Passed = passed, Checks = Checks.Count, Details = Checks, Error = error }));
        EditorApplication.Exit(passed ? 0 : 1);
    }
}

public sealed class HostRuntimeCounter : MonoBehaviour
{
    public int Awakes, Updates, Destroys;
    public bool SawActiveFrame, WasPlayingWhenDestroyed;
    public override void Awake() => Awakes++;
    public override void Update() { Updates++; SawActiveFrame |= Time.isFrameActive; }
    public override void OnDestroy() { Destroys++; WasPlayingWhenDestroyed = Application.isPlaying; }
}
