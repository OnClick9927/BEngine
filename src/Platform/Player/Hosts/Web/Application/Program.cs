using BEngine.Player;
using BEngine.Hosting;
using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using BEngine;
using BEngine.Platform;
using BEngine.Player.HotUpdate;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Player.Hosts.Web;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static IPlayerRuntimeSession? _session;
    private static WebGlGraphicsDevice? _graphics;
    private static double _previousTimestampMilliseconds;
    private static bool _hasPreviousTimestamp;
    private static int _width = 1;
    private static int _height = 1;
    private static long _renderedFrames;
    private static long _logEntries;
    private static double _updateMilliseconds, _renderMilliseconds;
    private static WebPhaseMemoryStatistics _updateMemory, _renderMemory;
    private static string _lastLog = string.Empty;
    private static string _lastError = string.Empty;
    private static readonly WebInputAdapter BrowserInput = new();
    private static readonly WebAccessibilityAdapter Accessibility = new();
    private static readonly List<WebStartupPhase> StartupPhases = new(8);

    public static void Main()
    {
        StartupPhases.Clear();
        PlayerStartupPerformanceDiagnostics.Reset();
        ManagedCodeStartupDiagnostics.Reset();
        var mainStarted = Stopwatch.GetTimestamp();
        var phaseStarted = mainStarted;
        void Mark(string phase)
        {
            var completed = Stopwatch.GetTimestamp();
            StartupPhases.Add(new(phase, Stopwatch.GetElapsedTime(phaseStarted, completed).TotalMilliseconds,
                Stopwatch.GetElapsedTime(mainStarted, completed).TotalMilliseconds));
            phaseStarted = completed;
        }
        try
        {
            BEngine.Platform.WebPlatform.Initialize();
            using (var status = JSHost.GlobalThis.GetPropertyAsJSObject("BEngineStartupStatus"))
                HostStartupSession.Begin(elapsedBeforeStart: TimeSpan.FromMilliseconds(
                    status?.GetPropertyAsDouble("elapsedMilliseconds") ?? 0), holdPresentation: true);
            Mark("platform");
            PlatformVideoDecoding.OpenVideoAsync = WebVideoDecoder.Open;
            WebInputAdapter.ConfigureClipboard();
            PlatformAccessibility.adapter = Accessibility;
            Debug.MessageLogged += OnMessageLogged;
            AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
            Mark("hostServices");
            var startupProfiling = IsStartupProfilingRequested();
            PlayerStartupPerformanceDiagnostics.Enabled = startupProfiling;
            ManagedCodeStartupDiagnostics.Enabled = startupProfiling;
            var projectPath = BEnginePlayer.ResolveDefaultProjectPath("/");
            Mark("resolveProject");
            _session = BEnginePlayer.CreateRuntimeSession(
                projectPath, declareAotInterpreter: true);
            Mark("createSession");
            var graphics = new WebGlGraphicsDevice();
            Mark("graphics");
            try { _session.AttachGraphicsDevice(graphics); }
            catch { graphics.Dispose(); throw; }
            _graphics = graphics;
            _session.Start();
            Mark("startSession");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BEngine Web Player startup failed:{Environment.NewLine}{exception}");
            try { Shutdown(); }
            catch (Exception cleanupFailure)
            {
                Console.Error.WriteLine($"BEngine Web Player startup cleanup failed:{Environment.NewLine}{cleanupFailure}");
            }
            throw;
        }
        finally
        {
            PlayerStartupPerformanceDiagnostics.Enabled = false;
            ManagedCodeStartupDiagnostics.Enabled = false;
        }
    }

    [JSExport]
    public static bool PumpStartup()
    {
        if (_session is null) throw new InvalidOperationException("The Player runtime is not initialized.");
        _session.Tick(TimeSpan.Zero);
        return _session.IsStartupPrepared;
    }

    [JSExport]
    public static double GetStartupProgress() => HostStartupEnvironment.Current?.Progress ?? 0;

    [JSExport]
    public static string GetStartupStage() => HostStartupEnvironment.Current?.Stage ?? "environment";

    [JSExport]
    public static void ConfirmStartupProgressPresented() => HostStartupEnvironment.Current?.ConfirmProgressPresented(1);

    [JSExport]
    public static void ContinueStartup() => HostStartupEnvironment.Current?.ReleasePresentation();

    private static bool IsStartupProfilingRequested()
    {
        using var location = JSHost.GlobalThis.GetPropertyAsJSObject("location");
        var search = location?.GetPropertyAsString("search");
        if (string.IsNullOrEmpty(search)) return false;
        foreach (var option in search.TrimStart('?').Split('&'))
            if (option is "bengine-profile=1" or "bengine-profile=true") return true;
        return false;
    }

    [JSExport]
    public static bool Tick(double timestampMilliseconds)
    {
        var session = _session;
        if (session is null || !Application.isPlaying)
        {
            Shutdown();
            return false;
        }
        if (!double.IsFinite(timestampMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(timestampMilliseconds));

        var elapsedMilliseconds = _hasPreviousTimestamp
            ? Math.Max(0, timestampMilliseconds - _previousTimestampMilliseconds)
            : 0;
        _hasPreviousTimestamp = true;
        _previousTimestampMilliseconds = timestampMilliseconds;
        var measure = SceneRuntimeDiagnostics.Enabled;
        var startedMemory = measure ? WebPhaseMemorySnapshot.Capture() : default;
        var started = measure ? Stopwatch.GetTimestamp() : 0;
        session.Tick(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        var updated = measure ? Stopwatch.GetTimestamp() : 0;
        var updatedMemory = measure ? WebPhaseMemorySnapshot.Capture() : default;
        session.Render(_width, _height);
        if (measure)
        {
            var rendered = Stopwatch.GetTimestamp();
            var renderedMemory = WebPhaseMemorySnapshot.Capture();
            _updateMilliseconds = Stopwatch.GetElapsedTime(started, updated).TotalMilliseconds;
            _renderMilliseconds = Stopwatch.GetElapsedTime(updated, rendered).TotalMilliseconds;
            _updateMemory = updatedMemory.Since(startedMemory);
            _renderMemory = renderedMemory.Since(updatedMemory);
        }
        _renderedFrames++;
        return true;
    }

    [JSExport]
    public static void Resize(int width, int height, double devicePixelRatio)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        DisplayUtility.Current.ReportDisplay(_width, _height, FullScreenMode.FullScreenWindow);
        DisplayUtility.Current.dpi = (Fix64)(Math.Max(0.25, devicePixelRatio) * 96.0);
    }

    [JSExport]
    public static void SetFocused(bool focused) => _session?.SetFocused(focused);

    [JSExport]
    public static void SetPaused(bool paused) => _session?.SetPaused(paused);
    [JSExport]
    public static void SetComposition(string text) => BrowserInput.SetComposition(text);
    [JSExport]
    public static string GetTextInputRequest() => BrowserInput.GetTextInputRequest();
    [JSExport]
    public static void SubmitTextEdit(string sessionId, string text, int anchor, int caret) =>
        BrowserInput.SubmitTextEdit(sessionId, text, anchor, caret);
    [JSExport]
    public static void ReportSafeArea(double left, double bottom, double right, double top) =>
        BrowserInput.ReportSafeArea(left, bottom, right, top);

    [JSExport]
    public static void KeyEvent(string code, bool pressed, string text) => BrowserInput.KeyEvent(code, pressed, text);

    [JSExport]
    public static void PointerEvent(int pointerId, string pointerType, int button, bool pressed,
        double x, double y, double deltaX, double deltaY, double pressure, double radius) =>
        BrowserInput.PointerEvent(pointerId, pointerType, button, pressed, x, y, deltaX, deltaY, pressure, radius);

    [JSExport]
    public static void PointerEnded(int pointerId, string pointerType, int button, double x, double y, bool canceled) =>
        BrowserInput.PointerEnded(pointerId, pointerType, button, x, y, canceled);

    [JSExport]
    public static void WheelEvent(double x, double y) => BrowserInput.WheelEvent(x, y);

    [JSExport]
    public static void ReportGamepads(string json) => BrowserInput.ReportGamepads(json);

    [JSExport]
    public static string GetDiagnostics() => CreateDiagnostics(includeColorReadback: false);

    /// <summary>Completed, non-overlapping Main phases; reading never invokes startup work or GPU readback.</summary>
    [JSExport]
    public static string GetStartupDiagnostics() => JsonSerializer.Serialize(
        StartupPhases.ToArray(), WebHostJsonContext.Default.WebStartupPhaseArray);

    /// <summary>Optional synchronous Main details. Parent/child durations overlap and must not be added.</summary>
    [JSExport]
    public static string GetStartupDetailDiagnostics() => JsonSerializer.Serialize(
        PlayerStartupPerformanceDiagnostics.Snapshot, WebHostJsonContext.Default.PlayerStartupPerformanceSampleArray);

    /// <summary>Opt-in managed-code CPU counters. Nested durations overlap; reading does not load code.</summary>
    [JSExport]
    public static string GetManagedCodeStartupDiagnostics() => JsonSerializer.Serialize(
        ManagedCodeStartupDiagnostics.Snapshot, WebHostJsonContext.Default.ManagedCodeStartupCounterArray);

    /// <summary>Opt-in, cumulative content measurements; nested durations are not additive.</summary>
    [JSExport]
    public static string GetContentDiagnostics() => JsonSerializer.Serialize(
        PlayerContentDiagnostics.Snapshot,
        WebHostJsonContext.Default.PlayerContentPerformanceSnapshot);

    /// <summary>Captures diagnostic pixels explicitly because GPU readback can stall rendering.</summary>
    [JSExport]
    public static string CaptureDiagnostics() => CreateDiagnostics(includeColorReadback: true);

    /// <summary>Enables optional runtime phase sampling; ordinary play leaves it disabled.</summary>
    [JSExport]
    public static void SetPerformanceProfiling(bool enabled)
    {
        SceneRuntimeDiagnostics.Enabled = enabled;
        CooperativeLoadingDiagnostics.SetEnabled(enabled);
        _updateMilliseconds = _renderMilliseconds = 0;
        _updateMemory = _renderMemory = default;
    }

    /// <summary>Returns the most recent sampled frame without GPU readback.</summary>
    [JSExport]
    public static string GetPerformanceDiagnostics() => JsonSerializer.Serialize(
        new WebPerformanceDiagnostics
        {
            Enabled = SceneRuntimeDiagnostics.Enabled,
            UpdateMilliseconds = _updateMilliseconds,
            RenderMilliseconds = _renderMilliseconds,
            UpdateMemory = _updateMemory,
            RenderMemory = _renderMemory,
            CooperativeWork = CooperativeWorkScheduler.Main.Snapshot,
            LoadingWork = SceneRuntimeDiagnostics.Enabled ? CooperativeLoadingDiagnostics.Snapshot : null,
            PendingCallbacks = BTaskScheduler.Main.PendingCallbackCount,
            Loading = FrameWorkBudget.isLoading,
            Runtime = SceneRuntimeDiagnostics.LastFrame,
            Rendering = _session?.LastRenderStatistics ?? default
        }, WebHostJsonContext.Default.WebPerformanceDiagnostics);

    private static string CreateDiagnostics(bool includeColorReadback)
    {
        var graphics = _graphics;
        var startup = _session?.StartupControl?.Current;
        var nonBackgroundPixels = 0;
        if (includeColorReadback && graphics is not null && _width > 0 && _height > 0)
        {
            var size = Math.Min(32, Math.Min(_width, _height));
            using var request = graphics.RequestColorReadback(
                new GraphicsRect((_width - size) / 2, (_height - size) / 2, size, size),
                _width, _height);
            if (request.TryGetResult(out var image))
            {
                var pixels = image!.Pixels.Span;
                for (var offset = 0; offset < pixels.Length; offset += 4)
                    if (pixels[offset] != 16 || pixels[offset + 1] != 18 || pixels[offset + 2] != 20)
                        nonBackgroundPixels++;
            }
        }
        return JsonSerializer.Serialize(new WebPlayerDiagnostics
        {
            Playing = Application.isPlaying,
            CanRender = _session?.CanRender == true,
            Backend = graphics?.Backend.ToString() ?? string.Empty,
            Width = _width,
            Height = _height,
            DrawCalls = graphics?.DrawStatistics.DrawCallCount ?? 0,
            Vertices = graphics?.DrawStatistics.VertexCount ?? 0,
            NonBackgroundPixels = nonBackgroundPixels,
            RenderedFrames = _renderedFrames,
            KeyEvents = BrowserInput.KeyEvents,
            PointerEvents = BrowserInput.PointerEvents,
            TouchEvents = BrowserInput.TouchEvents,
            WheelEvents = BrowserInput.WheelEvents,
            GamepadReports = BrowserInput.GamepadReports,
            TouchCount = Input.touchCount,
            GamepadCount = Input.gamepadCount,
            StartupPhase = startup?.Phase ?? string.Empty,
            StartupStatus = startup?.Status ?? string.Empty,
            StartupError = startup?.Error ?? string.Empty,
            LogEntries = _logEntries,
            LastLog = _lastLog,
            LastError = _lastError
        }, WebHostJsonContext.Default.WebPlayerDiagnostics);
    }

    [JSExport]
    public static void AdvanceStartup(bool preferInstalledContent)
    {
        _session?.StartupControl?.Advance(preferInstalledContent);
    }

    [JSExport]
    public static bool AccessibilityAction(double documentId, int nodeId, int action) =>
        Accessibility.Perform((long)documentId, nodeId, (PlatformAccessibilityAction)action);

    [JSExport]
    public static void Shutdown()
    {
        var session = _session;
        _session = null;
        _graphics = null;
        Debug.MessageLogged -= OnMessageLogged;
        BrowserInput.Reset();
        _hasPreviousTimestamp = false;
        _previousTimestampMilliseconds = 0;
        _renderedFrames = 0;
        if (ReferenceEquals(PlatformAccessibility.adapter, Accessibility))
            PlatformAccessibility.adapter = null;
        try { session?.Dispose(); }
        finally { HostStartupSession.Current?.Dispose(); }
    }

    private static void OnMessageLogged(LogEntry entry)
    {
        _logEntries++;
        _lastLog = entry.Message;
        if (entry.Type == LogType.Error)
        {
            _lastError = entry.Message;
            Console.Error.WriteLine(entry.ToDetailedString());
        }
        else
            Console.WriteLine(entry.ToDetailedString());
    }


}

internal sealed class WebPerformanceDiagnostics
{
    public bool Enabled { get; set; }
    public double UpdateMilliseconds { get; set; }
    public double RenderMilliseconds { get; set; }
    public WebPhaseMemoryStatistics UpdateMemory { get; set; }
    public WebPhaseMemoryStatistics RenderMemory { get; set; }
    public CooperativeWorkSnapshot CooperativeWork { get; set; }
    public LoadingWorkDiagnosticsSnapshot? LoadingWork { get; set; }
    public int PendingCallbacks { get; set; }
    public bool Loading { get; set; }
    public SceneRuntimeFrameStatistics Runtime { get; set; }
    public SceneRenderStatistics Rendering { get; set; }
}

internal sealed record WebStartupPhase(string Name, double Milliseconds, double TotalMilliseconds);

internal readonly record struct WebPhaseMemoryStatistics(
    long AllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);

// Allocation counts belong to this thread. Collection counts are process-wide
// events observed during the phase; they do not identify what triggered a GC.
internal readonly record struct WebPhaseMemorySnapshot(
    long AllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections)
{
    public static WebPhaseMemorySnapshot Capture() => new(
        GC.GetAllocatedBytesForCurrentThread(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    public WebPhaseMemoryStatistics Since(WebPhaseMemorySnapshot before) => new(
        AllocatedBytes - before.AllocatedBytes, Gen0Collections - before.Gen0Collections,
        Gen1Collections - before.Gen1Collections, Gen2Collections - before.Gen2Collections);
}

internal sealed class WebPlayerDiagnostics
{
    public bool Playing { get; set; }
    public bool CanRender { get; set; }
    public string Backend { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long DrawCalls { get; set; }
    public long Vertices { get; set; }
    public int NonBackgroundPixels { get; set; }
    public long RenderedFrames { get; set; }
    public long KeyEvents { get; set; }
    public long PointerEvents { get; set; }
    public long TouchEvents { get; set; }
    public long WheelEvents { get; set; }
    public long GamepadReports { get; set; }
    public int TouchCount { get; set; }
    public int GamepadCount { get; set; }
    public string StartupPhase { get; set; } = string.Empty;
    public string StartupStatus { get; set; } = string.Empty;
    public string StartupError { get; set; } = string.Empty;
    public long LogEntries { get; set; }
    public string LastLog { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WebStartupPhase[]))]
[JsonSerializable(typeof(PlayerStartupPerformanceSample[]))]
[JsonSerializable(typeof(ManagedCodeStartupCounter[]))]
[JsonSerializable(typeof(PlayerContentPerformanceSnapshot))]
[JsonSerializable(typeof(WebPlayerDiagnostics))]
[JsonSerializable(typeof(WebPerformanceDiagnostics))]
internal sealed partial class WebHostJsonContext : JsonSerializerContext;
