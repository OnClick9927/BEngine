using System.Runtime.InteropServices;
namespace BEngine.Editor;
internal sealed class WindowsEditorDesktopService : IEditorDesktopService
{
    public IReadOnlyList<Rect> GetWorkAreas()
    {
        try { return System.Windows.Forms.Screen.AllScreens.Select(screen => screen.WorkingArea).Select(area => new Rect(area.X, area.Y, area.Width, area.Height)).ToArray(); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { return []; }
    }
    public IDisposable? ObserveWindowMove(IntPtr window, Action<Vector2> updated, Action<Vector2?> completed, Func<Vector2?> pointerPosition) => Win32NativeMoveScope.TryCreate(window, updated, completed, pointerPosition);
    public bool TryShowMenu(IntPtr owner, Vector2 position, IReadOnlyList<GenericMenuItem> items, Rect? keepAliveScreenRect = null) => Win32GenericMenuPresenter.TryShow(owner, position, items, keepAliveScreenRect);
    public bool TrySamplePointerColor(out Color color)
    {
        color = default;
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var point)) return false;
        var deviceContext = GetDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero) return false;
        try
        {
            var value = GetPixel(deviceContext, point.X, point.Y);
            if (value == uint.MaxValue) return false;
            var red = (byte)(value & 0xff);
            var green = (byte)((value >> 8) & 0xff);
            var blue = (byte)((value >> 16) & 0xff);
            color = new Color((Fix64)(red / 255f), (Fix64)(green / 255f),
                (Fix64)(blue / 255f), 1);
            return true;
        }
        finally { ReleaseDC(IntPtr.Zero, deviceContext); }
    }

    public bool IsKeyPressed(KeyCode key) => OperatingSystem.IsWindows() && GetAsyncKeyState(key switch { KeyCode.Mouse0 => 0x01, KeyCode.Escape => 0x1B, _ => 0 }) < 0;

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out EditorNativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "GetDC", ExactSpelling = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC", ExactSpelling = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "GetPixel", ExactSpelling = true)]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);
}
