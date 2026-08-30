using System.Runtime.InteropServices;

namespace BEngine.Editor;

/// <summary>
/// Observes the real Win32 move loop. GLFW's event pump is blocked while a native title bar is
/// being dragged, so frame-based polling cannot provide a live dock preview on Windows.
/// </summary>
internal sealed class Win32NativeMoveScope : IDisposable
{
    private const int WindowProcedureIndex = -4;
    private const uint WindowMoving = 0x0216;
    private const uint WindowSizing = 0x0214;
    private const uint EnterSizeMove = 0x0231;
    private const uint ExitSizeMove = 0x0232;
    private const uint CancelMode = 0x001F;

    private readonly IntPtr _window;
    private readonly WindowProcedure _procedure;
    private readonly IntPtr _previousProcedure;
    private readonly Action<Vector2> _moveUpdated;
    private readonly Action<Vector2?> _moveCompleted;
    private readonly Func<Vector2?> _pointerPosition;
    private bool _moving;
    private bool _sizing;
    private bool _disposed;

    private Win32NativeMoveScope(
        IntPtr window,
        Action<Vector2> moveUpdated,
        Action<Vector2?> moveCompleted,
        Func<Vector2?> pointerPosition)
    {
        _window = window;
        _moveUpdated = moveUpdated;
        _moveCompleted = moveCompleted;
        _pointerPosition = pointerPosition;
        _procedure = WindowProc;
        var currentProcedure = GetWindowLongPtr(window, WindowProcedureIndex);
        if (currentProcedure == IntPtr.Zero)
            throw new InvalidOperationException("Could not read the native window procedure.");
        var procedure = Marshal.GetFunctionPointerForDelegate(_procedure);
        _previousProcedure = SetWindowLongPtr(window, WindowProcedureIndex, procedure);
        if (_previousProcedure == IntPtr.Zero)
            throw new InvalidOperationException("Could not observe native window movement.");
    }

    internal static Win32NativeMoveScope? TryCreate(
        IntPtr window,
        Action<Vector2> moveUpdated,
        Action<Vector2?> moveCompleted,
        Func<Vector2?>? pointerPosition = null)
    {
        ArgumentNullException.ThrowIfNull(moveUpdated);
        ArgumentNullException.ThrowIfNull(moveCompleted);
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero) return null;
        try
        {
            return new Win32NativeMoveScope(window, moveUpdated, moveCompleted,
                pointerPosition ?? CurrentPointerPosition);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window != IntPtr.Zero && _previousProcedure != IntPtr.Zero && IsWindow(_window))
            _ = SetWindowLongPtr(_window, WindowProcedureIndex, _previousProcedure);
        GC.KeepAlive(_procedure);
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case EnterSizeMove:
                _moving = false;
                _sizing = false;
                break;
            case WindowMoving:
                _moving = true;
                // WM_MOVING is the authoritative caption-drag signal. A per-monitor DPI change
                // may resize the window during the same move and must not turn it into a resize.
                _sizing = false;
                if (_pointerPosition() is { } movingPoint)
                    EditorCallbackDispatcher.Invoke(_moveUpdated, movingPoint,
                        nameof(Win32NativeMoveScope));
                break;
            case WindowSizing:
                if (!_moving) _sizing = true;
                break;
            case ExitSizeMove:
                CompleteMove(_moving && !_sizing);
                break;
            case CancelMode:
                if (_moving || _sizing) CompleteMove(false);
                break;
        }

        return CallWindowProc(_previousProcedure, window, message, wParam, lParam);
    }

    private void CompleteMove(bool commit)
    {
        Vector2? point = null;
        if (commit) point = _pointerPosition();
        _moving = false;
        _sizing = false;
        EditorCallbackDispatcher.Invoke(_moveCompleted, point, nameof(Win32NativeMoveScope));
    }

    private static Vector2? CurrentPointerPosition() =>
        ImGuiNativeWindow.TryGetPointerScreenPosition(out var point) ? point : null;

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "IsWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousProcedure,
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
