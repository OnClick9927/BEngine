using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform.DotNet;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize();
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
Check(typeof(EditorHostRuntime).Assembly == typeof(EditorObjectSnapshot).Assembly, "Authoring host operations belong to Core Editor");
Check(typeof(Scene).Assembly.GetType(typeof(EditorHostRuntime).FullName!) is null, "The authoring host bridge does not enter Core Runtime");
var inputContracts = new[] { typeof(CursorLockMode), typeof(CursorMode), typeof(GamepadAxis),
    typeof(GamepadButton), typeof(GamepadInfo), typeof(KeyCode), typeof(TextInputEdit),
    typeof(TextInputRequest), typeof(Touch), typeof(TouchPhase),
    typeof(BEngine.Platform.IInputService), typeof(BEngine.Platform.ITouchInputService),
    typeof(BEngine.Platform.ITouchInputSource) };
Check(inputContracts.All(type => type.Assembly.GetName().Name == "BEngine.Core" &&
      Type.GetType($"{type.FullName}, BEngine.Core") == type),
    "Input contracts belong to PlatformAbstractions and preserve old assembly type names");
var hostContracts = new[] { typeof(DeviceType), typeof(FullScreenMode), typeof(NetworkReachability),
    typeof(RuntimePlatform), typeof(SystemLanguage), typeof(BEngine.Platform.DisplayResolutionRequest),
    typeof(BEngine.Platform.IDisplayService), typeof(BEngine.Hosting.IHostOutput),
    typeof(BEngine.Hosting.INativeLibraryLoader), typeof(BEngine.Hosting.AssemblyImage),
    typeof(BEngine.Hosting.AssemblyImageDomainKind), typeof(BEngine.Hosting.AssemblyImageDomainOptions),
    typeof(BEngine.Hosting.IAssemblyImageDomain), typeof(BEngine.Hosting.IAssemblyDomainService),
    typeof(BEngine.Hosting.IHostSplashProgress) };
Check(hostContracts.All(type => type.Assembly.GetName().Name == "BEngine.Core" &&
      Type.GetType($"{type.FullName}, BEngine.Core") == type),
    "Display and host contracts belong to PlatformAbstractions and preserve old assembly type names");
foreach (var assembly in new[] { typeof(Scene).Assembly, typeof(EditorHostRuntime).Assembly })
    Check(!assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == Assembly.GetExecutingAssembly().GetName().Name),
        assembly.GetName().Name + " grants no private access to this independent consumer");
Check(!Application.isEditor, "A standalone Core consumer is not implicitly an editor");
Throws<InvalidOperationException>(EditorHostRuntime.EnterPlayMode, "Player hosts cannot enter editor Play Mode");
Throws<InvalidOperationException>(EditorHostRuntime.ReturnToEditMode, "Player hosts cannot reset editor mode");
Throws<InvalidOperationException>(EditorHostRuntime.PreparePlayClock, "Player clocks cannot be reset through the editor bridge");
Throws<InvalidOperationException>(() => EditorHostRuntime.AdvanceEditFrame(1), "Player clocks cannot advance as edit frames");
Throws<InvalidOperationException>(EditorHostRuntime.CompleteInputFrame, "Player input is outside the editor bridge");
var originalDirectory = Application.dataPath; var originalCompany = Application.companyName; var originalProduct = Application.productName;
var originalTime = Time.time; var originalFrame = Time.frameCount;
Throws<ArgumentException>(() => EditorHostRuntime.ConfigureApplication(" "), "Whitespace assets addresses are rejected");
Throws<ArgumentException>(() => EditorHostRuntime.ConfigureApplication(""), "Empty assets addresses are rejected");
Throws<ArgumentNullException>(() => EditorHostRuntime.ConfigureApplication(null!), "Null assets addresses are rejected");
Check(!Application.isEditor && Application.dataPath == originalDirectory, "Invalid bootstrap arguments preserve application state");
var absent = Path.Combine(output, "never-created", "Assets");
EditorHostRuntime.ConfigureApplication(absent);
Check(Application.isEditor && !Application.isPlaying && Application.dataPath == absent, "Bootstrap establishes edit mode and the exact assets address");
Check(!Directory.Exists(absent), "Bootstrap performs no directory creation");
Check(Application.companyName == originalCompany && Application.productName == originalProduct, "Bootstrap preserves identity");
Check(Time.time == originalTime && Time.frameCount == originalFrame, "Bootstrap does not reset the existing clock");
Check(typeof(Application).GetProperty(nameof(Application.isPlaying))!.SetMethod?.IsPublic != true &&
      typeof(Application).GetProperty(nameof(Application.isEditor))!.SetMethod?.IsPublic != true &&
      typeof(Application).GetProperty(nameof(Application.dataPath))!.SetMethod?.IsPublic != true,
    "Application state setters remain non-public");
Time.fixedDeltaTime = (Fix64)1 / 50; Time.maximumDeltaTime = (Fix64)1 / 10; Time.timeScale = 2;
EditorHostRuntime.PreparePlayClock();
Check(Time.time == 0 && Time.unscaledTime == 0 && Time.fixedTime == 0 && Time.frameCount == 0 && !Time.isFrameActive,
    "Preparation starts a fresh simulation clock between frames");
Check(Time.timeScale == 2 && Time.fixedDeltaTime == (Fix64)1 / 50 && Time.maximumDeltaTime == (Fix64)1 / 10,
    "Clock preparation retains simulation settings");
Check(!Application.isPlaying, "Clock preparation does not publish Play Mode");
EditorHostRuntime.AdvanceEditFrame((Fix64)1 / 4);
Check(Time.deltaTime == (Fix64)1 / 10 && Time.unscaledDeltaTime == (Fix64)1 / 4 && Time.time == Time.deltaTime,
    "Edit frames retain scaled, unscaled and maximum-delta semantics");
Check(Time.frameCount == 1 && Time.fixedTime == 0 && !Time.isFrameActive, "Edit frames do not open or simulate fixed steps");
EditorHostRuntime.AdvanceEditFrame(-1);
Check(Time.frameCount == 2 && Time.unscaledTime == (Fix64)1 / 4 && Time.deltaTime == 0,
    "Negative edit deltas retain the existing zero clamp");
var editState = Time.CaptureState(); var editTime = Time.time; var editCount = Time.frameCount;
EditorHostRuntime.EnterPlayMode(); EditorHostRuntime.EnterPlayMode();
Check(Application.isPlaying && Time.time == editTime && Time.frameCount == editCount,
    "Mode publication is idempotent and does not reset the prepared clock");
Throws<InvalidOperationException>(() => EditorHostRuntime.AdvanceEditFrame(1), "Play Mode rejects duplicate edit clock advancement");
Throws<InvalidOperationException>(EditorHostRuntime.PreparePlayClock, "A running game cannot have its clock reset through editor preparation");
Throws<InvalidOperationException>(() => EditorHostRuntime.ConfigureApplication("changed"), "A playing editor cannot be reconfigured");
Check(Application.dataPath == absent && Application.isPlaying && Time.frameCount == editCount, "Rejected operations preserve mode, address and clock");
Input.SetKeyState(KeyCode.A, true);
using (SceneRuntime.BeginFrame((Fix64)1 / 100))
{
    Throws<InvalidOperationException>(() => EditorHostRuntime.ConfigureApplication("changed"), "Bootstrap cannot interrupt an active runtime frame");
    Throws<InvalidOperationException>(EditorHostRuntime.EnterPlayMode, "Mode entry cannot interrupt an active runtime frame");
    Throws<InvalidOperationException>(EditorHostRuntime.ReturnToEditMode, "Mode exit cannot interrupt an active runtime frame");
    Throws<InvalidOperationException>(EditorHostRuntime.PreparePlayClock, "Clock preparation cannot interrupt an active runtime frame");
    Throws<InvalidOperationException>(() => EditorHostRuntime.AdvanceEditFrame(1), "Edit clock advancement cannot nest in a runtime frame");
    Throws<InvalidOperationException>(EditorHostRuntime.CompleteInputFrame, "Input cleanup cannot consume an active runtime frame's edges");
    Check(Time.isFrameActive && Application.isPlaying && Input.GetKeyDown(KeyCode.A), "Rejected frame-boundary operations preserve active clock and input");
}
EditorHostRuntime.CompleteInputFrame();
Check(!Input.GetKeyDown(KeyCode.A) && Input.GetKey(KeyCode.A), "Input completion clears edges and preserves held keys");
var stoppedTime = Time.time; var stoppedFrames = Time.frameCount;
EditorHostRuntime.ReturnToEditMode(); EditorHostRuntime.ReturnToEditMode();
Check(!Application.isPlaying && Time.time == stoppedTime && Time.frameCount == stoppedFrames,
    "Returning to edit mode is idempotent and does not silently restore clock state");
Time.RestoreState(editState);
Check(Time.time == editTime && Time.frameCount == editCount, "The host retains explicit ownership of clock restoration");
Input.SetKeyState(KeyCode.A, false); Input.SetKeyState(KeyCode.B, true);
Input.SetMouseDelta(2, 3); Input.SetMouseScroll(4, 5); Input.SetMousePosition(6, 7);
Input.AppendTextInput('x'); Input.SetComposition("composition"); Input.SetAxis("custom", (Fix64)1 / 2);
Input.SetGamepadConnected(0, true); Input.SetGamepadButtonState(0, GamepadButton.South, true); Input.SetGamepadAxis(0, GamepadAxis.LeftStickX, (Fix64)1 / 4);
Input.SetTouches([new Touch(1, new Vector2(3,4), Vector2.one, (Fix64)1/60, 1, TouchPhase.Began),
    new Touch(2, Vector2.zero, Vector2.zero, 0, 1, TouchPhase.Ended)]);
EditorHostRuntime.CompleteInputFrame();
Check(!Input.GetKeyUp(KeyCode.A) && !Input.GetKeyDown(KeyCode.B) && Input.GetKey(KeyCode.B), "Edit and paused input cleanup retain held-key semantics");
Check(Input.mouseX == 0 && Input.mouseY == 0 && Input.mouseScrollDelta == Vector2.zero && Input.mousePosition == new Vector2(6,7),
    "Input completion clears mouse deltas while retaining position");
Check(Input.inputString.Length == 0 && Input.compositionString == "composition" && Input.GetAxisRaw("custom") == (Fix64)1 / 2,
    "Input completion clears per-frame text while preserving composition and axes");
Check(Input.GetGamepadButton(0, GamepadButton.South) && !Input.GetGamepadButtonDown(0, GamepadButton.South) &&
      Input.GetGamepadAxis(0, GamepadAxis.LeftStickX) == (Fix64)1 / 4, "Input completion preserves connected gamepad state and axes");
Check(Input.touchCount == 1 && Input.touches[0].phase == TouchPhase.Stationary && Input.touches[0].deltaPosition == Vector2.zero,
    "Input completion advances touch phases and removes ended touches");
Input.ResetInputAxes(); Input.SetTouches([]);
using var scene = new Scene("host frames"); var counter = scene.CreateGameObject().AddComponent<HostCounter>();
var runtime = new SceneRuntime(scene); var originalCounter = counter.Updates;
EditorHostRuntime.AdvanceEditFrame((Fix64)1 / 60);
Check(counter.Updates == originalCounter && !runtime.IsRunning, "Advancing the edit clock does not start or tick scenes");
EditorHostRuntime.PreparePlayClock(); EditorHostRuntime.EnterPlayMode(); runtime.Start();
var before = Time.frameCount;
SceneRuntime.TickAll([runtime], (Fix64)1 / 60); EditorHostRuntime.CompleteInputFrame();
Check(counter.Updates == 1 && Time.frameCount == before + 1 && !Time.isFrameActive, "Normal runtime ticking advances exactly one shared frame");
Input.SetKeyState(KeyCode.C, true); EditorHostRuntime.CompleteInputFrame();
Check(counter.Updates == 1 && Time.frameCount == before + 1 && !Input.GetKeyDown(KeyCode.C), "Paused input updates do not simulate a frame");
using (SceneRuntime.BeginFrame(Time.fixedDeltaTime)) runtime.Tick(Time.fixedDeltaTime);
EditorHostRuntime.CompleteInputFrame();
Check(counter.Updates == 2 && Time.frameCount == before + 2, "A manual runtime step advances exactly one frame");
runtime.Stop(); EditorHostRuntime.ReturnToEditMode();
EditorHostRuntime.PreparePlayClock(); EditorHostRuntime.EnterPlayMode(); runtime.Start(); runtime.Tick((Fix64)1/60);
Check(counter.Updates == 3, "Resetting a clock does not rewind runtime dispatch identity across repeated Play Mode runs");
runtime.Stop(); EditorHostRuntime.ReturnToEditMode();
var pending = new BTaskCompletionSource(); var resumed = false;
async BTask WaitForPump() { await pending.Task; resumed = true; }
var wait = WaitForPump();
EditorHostRuntime.AdvanceEditFrame(0); EditorHostRuntime.CompleteInputFrame();
Check(!resumed, "Host clock and input operations do not pump unrelated asynchronous work");
pending.TrySetResult(); BTaskScheduler.Main.Tick(TimeSpan.Zero); wait.GetAwaiter().GetResult();
Check(resumed, "Task scheduling remains explicitly owned by the host");
_ = Measurement.Run(); var measurement = Measurement.Run();
Check(measurement.Bytes == 0, "Warmed edit-frame and input-completion pairs allocate zero managed bytes");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Host" or "BEngine.Application"),
    "The bridge runs without graphics, UI, workbench or a game framework");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks,
    Measurement = new { Pairs = 100000, AllocatedBytes = measurement.Bytes, Milliseconds = measurement.Ms } }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"EDITOR_HOST_RUNTIME_OK {checks.Count}");

public sealed class HostCounter : MonoBehaviour { public int Updates; public override void Update() => Updates++; }
static class Measurement
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long Bytes, double Ms) Run()
    {
        var start = Stopwatch.GetTimestamp(); var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100000; i++) { EditorHostRuntime.AdvanceEditFrame(0); EditorHostRuntime.CompleteInputFrame(); }
        return (GC.GetAllocatedBytesForCurrentThread() - bytes, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}
