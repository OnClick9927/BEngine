using System.Runtime.InteropServices;
using System.Text;

namespace BEngine.Editor;

/// <summary>
/// Ends the current Win32 menu loop after the pointer leaves both the native menu windows and
/// the control/menu-bar area that opened it. The hook is thread-local and always uninstalled.
/// </summary>
internal sealed partial class Win32MenuMouseExitScope : IDisposable
{
    private const int WhMessageFilter = -1;
    private const int MessageFilterMenu = 2;
    private const uint WmMouseMove = 0x0200;
    private const uint WmNcMouseMove = 0x00A0;
    private const string MenuWindowClass = "#32768";

    private readonly NativeMenuMouseExitPolicy _policy;
    private readonly NativeScreenRect? _keepAliveRect;
    private readonly HookProcedure _procedure;
    private IntPtr _hook;
    private bool _disposed;

    private Win32MenuMouseExitScope(NativeScreenRect? keepAliveRect)
    {
        _keepAliveRect = keepAliveRect;
        _policy = new NativeMenuMouseExitPolicy(PointerInsideKeepAlive());
        _procedure = FilterMessage;
        _hook = SetWindowsHookEx(WhMessageFilter, _procedure, IntPtr.Zero, GetCurrentThreadId());
    }

    internal static Win32MenuMouseExitScope? TryInstall(NativeScreenRect? keepAliveRect = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var scope = new Win32MenuMouseExitScope(keepAliveRect);
            if (scope._hook != IntPtr.Zero) return scope;
            scope.Dispose();
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        GC.KeepAlive(_procedure);
    }

    private IntPtr FilterMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (!_disposed && code == MessageFilterMenu && lParam != IntPtr.Zero)
            {
                var message = Marshal.PtrToStructure<NativeMessage>(lParam);
                if (message.Message is WmMouseMove or WmNcMouseMove)
                {
                    var insideNativeMenu = IsNativeMenuWindow(WindowFromPoint(message.Point));
                    var insideKeepAlive = _keepAliveRect?.Contains(message.Point.X, message.Point.Y) == true;
                    if (_policy.Observe(insideNativeMenu, insideKeepAlive)) _ = EndMenu();
                }
            }
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report("Win32 menu mouse-exit hook", exception);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool PointerInsideKeepAlive() =>
        _keepAliveRect is { } rect && GetCursorPos(out var point) && rect.Contains(point.X, point.Y);

    private static bool IsNativeMenuWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        var className = new StringBuilder(32);
        return GetClassName(window, className, className.Capacity) > 0 &&
               className.ToString().Equals(MenuWindowClass, StringComparison.Ordinal);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeMessage
    {
        internal readonly IntPtr Window;
        internal readonly uint Message;
        internal readonly IntPtr WParam;
        internal readonly IntPtr LParam;
        internal readonly uint Time;
        internal readonly NativePoint Point;
        internal readonly uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct NativePoint(int X, int Y);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", ExactSpelling = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hook,
        HookProcedure procedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", EntryPoint = "CallNextHookEx", ExactSpelling = true)]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "EndMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndMenu();

    [LibraryImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static partial IntPtr WindowFromPoint(NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
}
