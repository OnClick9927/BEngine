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
using BEngine.Player;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Player.Hosts.Web;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static PlayerRuntimeSession? _session;
    private static WebGlGraphicsDevice? _graphics;
    private static double _previousTimestampMilliseconds;
    private static bool _hasPreviousTimestamp;
    private static int _width = 1;
    private static int _height = 1;
    private static long _renderedFrames;
    private static long _keyEvents;
    private static long _pointerEvents;
    private static long _touchEvents;
    private static long _wheelEvents;
    private static long _gamepadReports;
    private static long _logEntries;
    private static double _updateMilliseconds, _renderMilliseconds;
    private static WebPhaseMemoryStatistics _updateMemory, _renderMemory;
    private static string _lastLog = string.Empty;
    private static string _lastError = string.Empty;
    private static readonly Dictionary<int, Touch> Touches = [];
    private static readonly HashSet<int> ConnectedGamepads = [];
    private static readonly WebAccessibilityAdapter Accessibility = new();

    public static void Main()
    {
        try
        {
            BEngine.Platform.WebPlatform.Initialize();
            PlatformVideoDecoding.OpenVideoAsync = WebVideoDecoder.Open;
            if (PlatformRuntime.GetService<IInputService>() is PlatformInputService input)
            { input.ReadClipboard = ReadClipboard; input.WriteClipboard = WriteClipboard; }
            PlatformAccessibility.adapter = Accessibility;
            Debug.MessageLogged += OnMessageLogged;
            AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
            var projectPath = BEnginePlayer.ResolveDefaultProjectPath("/");
            _session = BEnginePlayer.CreateRuntimeSession(
                projectPath, declareAotInterpreter: true);
            _graphics = new WebGlGraphicsDevice();
            _session.AttachGraphicsDevice(_graphics);
            _session.Start();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BEngine Web Player startup failed:{Environment.NewLine}{exception}");
            throw;
        }
    }

    [JSExport]
    public static string GetTestResult() => System.IO.File.Exists("/bengine-tests.json") ? System.IO.File.ReadAllText("/bengine-tests.json") : "";

    [JSExport]
    public static bool Tick(double timestampMilliseconds)
    {
        var session = _session;
        if (session is null || !BEngine.Application.isPlaying)
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
    public static void SetComposition(string text) => Input.SetComposition(text);
    [JSExport]
    public static string GetTextInputRequest() => Input.textInputRequest is { } request
        ? JsonSerializer.Serialize(new WebTextInputRequest(request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Text, request.Anchor, request.Caret, request.Password, request.Multiline), WebHostJsonContext.Default.WebTextInputRequest)
        : string.Empty;
    [JSExport]
    public static void SubmitTextEdit(string sessionId, string text, int anchor, int caret)
    {
        if (long.TryParse(sessionId, System.Globalization.CultureInfo.InvariantCulture, out var id))
            Input.SubmitTextEdit(new(id, text, anchor, caret));
    }
    [JSExport]
    public static void ReportSafeArea(double left, double bottom, double right, double top) =>
        DisplayUtility.Current.ReportSafeArea(new((Fix64)left, (Fix64)bottom, (Fix64)right, (Fix64)top));
    [JSImport("globalThis.BEngineText.readClipboard")]
    private static partial string ReadClipboard();
    [JSImport("globalThis.BEngineText.writeClipboard")]
    private static partial bool WriteClipboard(string text);

    [JSExport]
    public static void KeyEvent(string code, bool pressed, string text)
    {
        _keyEvents++;
        if (TryMapKey(code, out var key)) Input.SetKeyState(key, pressed);
        if (pressed && !string.IsNullOrEmpty(text))
            foreach (var character in text) Input.AppendTextInput(character);
    }

    [JSExport]
    public static void PointerEvent(int pointerId, string pointerType, int button, bool pressed,
        double x, double y, double deltaX, double deltaY, double pressure, double radius)
    {
        _pointerEvents++;
        if (string.Equals(pointerType, "touch", StringComparison.OrdinalIgnoreCase))
        {
            _touchEvents++;
            var phase = Touches.ContainsKey(pointerId) ? TouchPhase.Moved : TouchPhase.Began;
            Touches[pointerId] = new Touch(pointerId,
                new Vector2((Fix64)x, (Fix64)y),
                new Vector2((Fix64)deltaX, (Fix64)deltaY),
                (Fix64)Math.Max(0, (double)Time.unscaledDeltaTime),
                1, phase, (Fix64)Math.Max(0, pressure), Fix64.One,
                (Fix64)Math.Max(0, radius), Fix64.Zero);
            Input.SetTouches(Touches.Values);
            return;
        }

        Input.SetMousePosition((Fix64)x, (Fix64)y);
        Input.SetMouseDelta((Fix64)deltaX, (Fix64)deltaY);
        if (button is >= 0 and <= 6) Input.SetKeyState((KeyCode)((int)KeyCode.Mouse0 + button), pressed);
    }

    [JSExport]
    public static void PointerEnded(int pointerId, string pointerType, int button,
        double x, double y, bool canceled)
    {
        _pointerEvents++;
        if (string.Equals(pointerType, "touch", StringComparison.OrdinalIgnoreCase))
        {
            _touchEvents++;
            Touches[pointerId] = new Touch(pointerId,
                new Vector2((Fix64)x, (Fix64)y), Vector2.zero, Fix64.Zero, 1,
                canceled ? TouchPhase.Canceled : TouchPhase.Ended);
            Input.SetTouches(Touches.Values);
            Touches.Remove(pointerId);
            return;
        }
        Input.SetMousePosition((Fix64)x, (Fix64)y);
        if (button is >= 0 and <= 6) Input.SetKeyState((KeyCode)((int)KeyCode.Mouse0 + button), false);
    }

    [JSExport]
    public static void WheelEvent(double x, double y)
    {
        _wheelEvents++;
        Input.SetMouseScroll((Fix64)x, (Fix64)(-y));
    }

    [JSExport]
    public static void ReportGamepads(string json)
    {
        _gamepadReports++;
        var snapshots = JsonSerializer.Deserialize(
            json, WebHostJsonContext.Default.GamepadStateSnapshotArray) ?? [];
        var current = snapshots.Select(item => item.Index).ToHashSet();
        foreach (var disconnected in ConnectedGamepads.Except(current).ToArray())
        {
            Input.SetGamepadConnected(disconnected, false);
            ConnectedGamepads.Remove(disconnected);
        }
        foreach (var gamepad in snapshots)
        {
            ConnectedGamepads.Add(gamepad.Index);
            Input.SetGamepadConnected(gamepad.Index, true, gamepad.Id);
            SetGamepadAxes(gamepad);
            SetGamepadButtons(gamepad);
        }
    }

    [JSExport]
    public static string GetDiagnostics() => CreateDiagnostics(includeColorReadback: false);

    /// <summary>Captures diagnostic pixels explicitly because GPU readback can stall rendering.</summary>
    [JSExport]
    public static string CaptureDiagnostics() => CreateDiagnostics(includeColorReadback: true);

    /// <summary>Enables optional runtime phase sampling; ordinary play leaves it disabled.</summary>
    [JSExport]
    public static void SetPerformanceProfiling(bool enabled)
    {
        SceneRuntimeDiagnostics.Enabled = enabled;
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
            Runtime = SceneRuntimeDiagnostics.LastFrame,
            Rendering = _session?.LastRenderStatistics ?? default
        }, WebHostJsonContext.Default.WebPerformanceDiagnostics);

    private static string CreateDiagnostics(bool includeColorReadback)
    {
        var graphics = _graphics;
        var startup = _session?.StartupFlow?.Current;
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
            Playing = BEngine.Application.isPlaying,
            CanRender = _session?.CanRender == true,
            Backend = graphics?.Backend.ToString() ?? string.Empty,
            Width = _width,
            Height = _height,
            DrawCalls = graphics?.DrawStatistics.DrawCallCount ?? 0,
            Vertices = graphics?.DrawStatistics.VertexCount ?? 0,
            NonBackgroundPixels = nonBackgroundPixels,
            RenderedFrames = _renderedFrames,
            KeyEvents = _keyEvents,
            PointerEvents = _pointerEvents,
            TouchEvents = _touchEvents,
            WheelEvents = _wheelEvents,
            GamepadReports = _gamepadReports,
            TouchCount = Input.touchCount,
            GamepadCount = Input.gamepadCount,
            StartupPhase = startup?.Phase.ToString() ?? string.Empty,
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
        var flow = _session?.StartupFlow;
        if (flow is null) return;
        var current = flow.Current;
        if (current.CanCheckForUpdates) flow.CheckForUpdates();
        else if (current.RequiresUpdateConfirmation)
        {
            if (preferInstalledContent && current.CanDeclineUpdate)
                flow.DeclineUpdate();
            else
                flow.ConfirmUpdate();
        }
        else if (current.CanEnterGame) flow.EnterGame();
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
        Touches.Clear();
        ConnectedGamepads.Clear();
        _hasPreviousTimestamp = false;
        _previousTimestampMilliseconds = 0;
        _renderedFrames = 0;
        _keyEvents = 0;
        _pointerEvents = 0;
        _touchEvents = 0;
        _wheelEvents = 0;
        _gamepadReports = 0;
        if (ReferenceEquals(PlatformAccessibility.adapter, Accessibility))
            PlatformAccessibility.adapter = null;
        session?.Dispose();
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

    private static void SetGamepadAxes(GamepadStateSnapshot gamepad)
    {
        var axes = gamepad.Axes ?? [];
        if (axes.Length > 0) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.LeftStickX, (Fix64)axes[0]);
        if (axes.Length > 1) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.LeftStickY, (Fix64)(-axes[1]));
        if (axes.Length > 2) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.RightStickX, (Fix64)axes[2]);
        if (axes.Length > 3) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.RightStickY, (Fix64)(-axes[3]));
        var buttons = gamepad.Buttons ?? [];
        if (buttons.Length > 6) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.LeftTrigger, (Fix64)buttons[6]);
        if (buttons.Length > 7) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.RightTrigger, (Fix64)buttons[7]);
    }

    private static void SetGamepadButtons(GamepadStateSnapshot gamepad)
    {
        var buttons = gamepad.Buttons ?? [];
        (int Index, GamepadButton Button)[] mappings =
        [
            (0, GamepadButton.South), (1, GamepadButton.East), (2, GamepadButton.West),
            (3, GamepadButton.North), (4, GamepadButton.LeftShoulder),
            (5, GamepadButton.RightShoulder), (8, GamepadButton.Back),
            (9, GamepadButton.Start), (10, GamepadButton.LeftStick),
            (11, GamepadButton.RightStick), (12, GamepadButton.DPadUp),
            (13, GamepadButton.DPadDown), (14, GamepadButton.DPadLeft),
            (15, GamepadButton.DPadRight), (16, GamepadButton.Home)
        ];
        foreach (var mapping in mappings)
            Input.SetGamepadButtonState(gamepad.Index, mapping.Button,
                mapping.Index < buttons.Length && buttons[mapping.Index] >= 0.5);
    }

    private static bool TryMapKey(string code, out KeyCode key)
    {
        key = code switch
        {
            "Space" => KeyCode.Space, "Enter" => KeyCode.Return, "Escape" => KeyCode.Escape,
            "Tab" => KeyCode.Tab, "Backspace" => KeyCode.Backspace, "Delete" => KeyCode.Delete,
            "Home" => KeyCode.Home, "End" => KeyCode.End, "PageUp" => KeyCode.PageUp,
            "PageDown" => KeyCode.PageDown, "Insert" => KeyCode.Insert,
            "CapsLock" => KeyCode.CapsLock, "NumLock" => KeyCode.Numlock,
            "PrintScreen" => KeyCode.Print, "ScrollLock" => KeyCode.ScrollLock,
            "Pause" => KeyCode.Pause, "Backquote" => KeyCode.BackQuote, "Minus" => KeyCode.Minus,
            "Equal" => KeyCode.Equals, "BracketLeft" => KeyCode.LeftBracket,
            "BracketRight" => KeyCode.RightBracket, "Backslash" => KeyCode.Backslash,
            "Semicolon" => KeyCode.Semicolon, "Quote" => KeyCode.Quote, "Comma" => KeyCode.Comma,
            "Period" => KeyCode.Period, "Slash" => KeyCode.Slash,
            "ShiftLeft" => KeyCode.LeftShift, "ShiftRight" => KeyCode.RightShift,
            "ControlLeft" => KeyCode.LeftControl, "ControlRight" => KeyCode.RightControl,
            "AltLeft" => KeyCode.LeftAlt, "AltRight" => KeyCode.RightAlt,
            "MetaLeft" => KeyCode.LeftCommand, "MetaRight" => KeyCode.RightCommand,
            "ArrowUp" => KeyCode.UpArrow, "ArrowDown" => KeyCode.DownArrow,
            "ArrowLeft" => KeyCode.LeftArrow, "ArrowRight" => KeyCode.RightArrow,
            _ => KeyCode.None
        };
        if (key != KeyCode.None) return true;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) &&
            code[3] is >= 'A' and <= 'Z')
        {
            key = (KeyCode)((int)KeyCode.A + code[3] - 'A');
            return true;
        }
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) &&
            code[5] is >= '0' and <= '9')
        {
            key = (KeyCode)((int)KeyCode.Alpha0 + code[5] - '0');
            return true;
        }
        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out var function) &&
            function is >= 1 and <= 15)
        {
            key = (KeyCode)((int)KeyCode.F1 + function - 1);
            return true;
        }
        return false;
    }

}

internal sealed class GamepadStateSnapshot
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public double[]? Axes { get; set; }
    public double[]? Buttons { get; set; }
}

internal sealed class WebPerformanceDiagnostics
{
    public bool Enabled { get; set; }
    public double UpdateMilliseconds { get; set; }
    public double RenderMilliseconds { get; set; }
    public WebPhaseMemoryStatistics UpdateMemory { get; set; }
    public WebPhaseMemoryStatistics RenderMemory { get; set; }
    public SceneRuntimeFrameStatistics Runtime { get; set; }
    public SceneRenderStatistics Rendering { get; set; }
}

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

[SupportedOSPlatform("browser")]
internal sealed partial class WebAccessibilityAdapter : IPlatformAccessibilityAdapter
{
    private readonly Dictionary<long, Func<int, PlatformAccessibilityAction, bool>> _actions = [];

    public bool IsScreenReaderActive => HasAccessibilitySupport();
    public void AttachWindow(nint nativeHandle) { }

    public void UpdateHierarchy(long documentId, PlatformAccessibilitySnapshot snapshot,
        Func<int, PlatformAccessibilityAction, bool> performAction)
    {
        _actions[documentId] = performAction;
        var document = new WebAccessibilityDocument
        {
            DocumentId = documentId,
            Revision = snapshot.Revision,
            Nodes = snapshot.Nodes.Select(node => new WebAccessibilityNode
            {
                Id = node.Id,
                ParentId = node.ParentId,
                Role = (int)node.Role,
                Name = node.Name,
                Description = node.Description,
                Value = node.Value,
                State = (int)node.State,
                X = node.Bounds.X,
                Y = node.Bounds.Y,
                Width = node.Bounds.Width,
                Height = node.Bounds.Height,
                MinimumValue = node.MinimumValue,
                MaximumValue = node.MaximumValue,
                LiveRegion = (int)node.LiveRegion
            }).ToArray()
        };
        UpdateAccessibility(JsonSerializer.Serialize(document, WebHostJsonContext.Default.WebAccessibilityDocument));
    }

    public void RemoveHierarchy(long documentId)
    {
        _actions.Remove(documentId);
        RemoveAccessibility(documentId);
    }

    public void Notify(PlatformAccessibilityEvent accessibilityEvent)
    {
        if (accessibilityEvent.Kind == PlatformAccessibilityEventKind.Announcement &&
            !string.IsNullOrWhiteSpace(accessibilityEvent.Message))
            AnnounceAccessibility(accessibilityEvent.Message, (int)accessibilityEvent.LiveRegion);
    }

    internal bool Perform(long documentId, int nodeId, PlatformAccessibilityAction action) =>
        _actions.TryGetValue(documentId, out var callback) && callback(nodeId, action);

    public void Dispose()
    {
        foreach (var documentId in _actions.Keys.ToArray()) RemoveAccessibility(documentId);
        _actions.Clear();
    }

    [JSImport("globalThis.BEngineAccessibility.hasSupport")]
    private static partial bool HasAccessibilitySupport();
    [JSImport("globalThis.BEngineAccessibility.update")]
    private static partial void UpdateAccessibility(string json);
    [JSImport("globalThis.BEngineAccessibility.remove")]
    private static partial void RemoveAccessibility(double documentId);
    [JSImport("globalThis.BEngineAccessibility.announce")]
    private static partial void AnnounceAccessibility(string message, int liveRegion);
}

internal sealed class WebAccessibilityDocument
{
    public long DocumentId { get; set; }
    public long Revision { get; set; }
    public WebAccessibilityNode[] Nodes { get; set; } = [];
}

internal sealed class WebAccessibilityNode
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int Role { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int State { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float? MinimumValue { get; set; }
    public float? MaximumValue { get; set; }
    public int LiveRegion { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GamepadStateSnapshot[]))]
[JsonSerializable(typeof(WebPlayerDiagnostics))]
[JsonSerializable(typeof(WebPerformanceDiagnostics))]
[JsonSerializable(typeof(WebAccessibilityDocument))]
[JsonSerializable(typeof(WebTextInputRequest))]
internal sealed partial class WebHostJsonContext : JsonSerializerContext;
internal sealed record WebTextInputRequest(string Id, string Text, int Anchor, int Caret, bool Password, bool Multiline);
