using System.Runtime.InteropServices;

namespace BEngine.Editor;

internal static class EditorColorEyedropper
{
    private const int LeftButton = 0x01;
    private const int EscapeKey = 0x1B;
    private static Action<Color>? _picked;
    private static bool _waitingForRelease;

    internal static bool active => _picked is not null;

    internal static void Begin(Action<Color> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        Cancel();
        _picked = picked;
        _waitingForRelease = IsKeyDown(LeftButton);
        EditorApplication.update += Poll;
    }

    internal static void Cancel()
    {
        EditorApplication.update -= Poll;
        _picked = null;
        _waitingForRelease = false;
    }

    private static void Poll()
    {
        if (_picked is null) return;
        if (IsKeyDown(EscapeKey))
        {
            Cancel();
            return;
        }
        var pressed = IsKeyDown(LeftButton);
        if (_waitingForRelease)
        {
            if (!pressed) _waitingForRelease = false;
            return;
        }
        if (!pressed || !TrySample(out var color)) return;
        var callback = _picked;
        Cancel();
        if (callback is not null)
            EditorFeatureGuard.Invoke("ColorEyedropper.Picked", () => callback(color));
    }

    private static bool TrySample(out Color color)
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

    private static bool IsKeyDown(int key) => OperatingSystem.IsWindows() && GetAsyncKeyState(key) < 0;

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
