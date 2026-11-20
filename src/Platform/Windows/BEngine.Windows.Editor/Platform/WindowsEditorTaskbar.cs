using System.ComponentModel;
using System.Runtime.InteropServices;
using Icon = System.Drawing.Icon;

namespace BEngine.Editor;

internal static class WindowsEditorTaskbar
{
    private const uint WmSetIcon = 0x0080;
    private const int ClassLargeIcon = -14;
    private const int ClassSmallIcon = -34;
    private static readonly Lazy<int> Identity = new(() => SetCurrentProcessExplicitAppUserModelID("BEngine.Editor"));

    // GLFW shares one window class between the loading, main and floating windows.
    // Class icon handles must remain alive even after any individual window closes.
    // These two icons are owned by the process and released by Windows at exit.
    private static readonly Lazy<Icon> SmallIcon = new(() => LoadIcon(49, 50));
    private static readonly Lazy<Icon> LargeIcon = new(() => LoadIcon(11, 12));

    internal static void InitializeProcess()
    {
        if (OperatingSystem.IsWindows()) Marshal.ThrowExceptionForHR(Identity.Value);
    }

    internal static void Apply(IntPtr window)
    {
        if (window == IntPtr.Zero) throw new ArgumentException("A native editor window is required.", nameof(window));
        var small = SmallIcon.Value.Handle;
        var large = LargeIcon.Value.Handle;
        SetClassIcon(window, ClassSmallIcon, small);
        SetClassIcon(window, ClassLargeIcon, large);
        SendMessage(window, WmSetIcon, IntPtr.Zero, small);
        SendMessage(window, WmSetIcon, new IntPtr(1), large);
    }

    private static Icon LoadIcon(int widthMetric, int heightMetric)
    {
        // Project resource registration and the working directory are not available
        // consistently during bootstrap. Keep the taskbar icon in the editor itself.
        var resource = widthMetric == 49 ? "BEngine.Editor.BEngine.Dark.ico" : "BEngine.Editor.BEngine.ico";
        using var stream = typeof(WindowsEditorTaskbar).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The embedded BEngine taskbar icon is missing.");
        using var icon = new Icon(stream, GetSystemMetrics(widthMetric), GetSystemMetrics(heightMetric));
        return (Icon)icon.Clone();
    }

    private static void SetClassIcon(IntPtr window, int index, IntPtr icon)
    {
        if (IntPtr.Size == 8) SetClassLongPtr(window, index, icon);
        else SetClassLong(window, index, icon.ToInt32());
        var error = Marshal.GetLastWin32Error();
        if (error != 0) throw new Win32Exception(error, "Could not set the BEngine window class icon.");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetClassLongW", ExactSpelling = true, SetLastError = true)]
    private static extern uint SetClassLong(IntPtr window, int index, int value);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
