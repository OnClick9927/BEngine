using System.Runtime.InteropServices;
using BEngine.Editor;

namespace BEngine.ExampleTests.GpuDockWindowStates;

internal static class Win32NativeMoveScopeTests
{
    private const uint WindowMoving = 0x0216;
    private const uint WindowSizing = 0x0214;
    private const uint EnterSizeMove = 0x0231;
    private const uint ExitSizeMove = 0x0232;
    private const uint PopupWindow = 0x80000000;

    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = CreateWindowEx(0, "STATIC", "BEngine native move test", PopupWindow,
            0, 0, 120, 80, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        Require(window != IntPtr.Zero, "Could not create the native move-loop test window.");
        var nativeRect = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(new NativeRect(10, 10, 130, 90), nativeRect, false);
            var pointer = new Vector2(420, 260);
            var previews = new List<Vector2>();
            var completions = new List<Vector2?>();
            using (var scope = Win32NativeMoveScope.TryCreate(window, previews.Add,
                       completions.Add, () => pointer))
            {
                Require(scope is not null, "The Win32 native move observer was not installed.");
                _ = SendMessage(window, EnterSizeMove, IntPtr.Zero, IntPtr.Zero);
                _ = SendMessage(window, WindowMoving, IntPtr.Zero, nativeRect);
                _ = SendMessage(window, ExitSizeMove, IntPtr.Zero, IntPtr.Zero);
            }

            Require(previews.SequenceEqual([pointer]),
                "WM_MOVING did not emit the live native dock-preview pointer.");
            Require(completions.SequenceEqual([new Vector2?(pointer)]),
                "WM_EXITSIZEMOVE did not commit the native title-bar dock drag.");

            previews.Clear();
            completions.Clear();
            using (var scope = Win32NativeMoveScope.TryCreate(window, previews.Add,
                       completions.Add, () => pointer))
            {
                Require(scope is not null, "The cross-DPI native move observer was not installed.");
                _ = SendMessage(window, EnterSizeMove, IntPtr.Zero, IntPtr.Zero);
                _ = SendMessage(window, WindowMoving, IntPtr.Zero, nativeRect);
                _ = SendMessage(window, WindowSizing, IntPtr.Zero, nativeRect);
                _ = SendMessage(window, ExitSizeMove, IntPtr.Zero, IntPtr.Zero);
            }

            Require(previews.SequenceEqual([pointer]) &&
                    completions.SequenceEqual([new Vector2?(pointer)]),
                "A per-monitor DPI size adjustment canceled an active native caption dock drag.");

            previews.Clear();
            completions.Clear();
            using (var scope = Win32NativeMoveScope.TryCreate(window, previews.Add,
                       completions.Add, () => pointer))
            {
                Require(scope is not null, "The Win32 resize observer was not installed.");
                _ = SendMessage(window, EnterSizeMove, IntPtr.Zero, IntPtr.Zero);
                _ = SendMessage(window, WindowSizing, IntPtr.Zero, nativeRect);
                _ = SendMessage(window, ExitSizeMove, IntPtr.Zero, IntPtr.Zero);
            }

            Require(previews.Count == 0 && completions.Count == 1 && completions[0] is null,
                "A native border resize was incorrectly committed as a dock drag.");
        }
        finally
        {
            Marshal.FreeHGlobal(nativeRect);
            _ = DestroyWindow(window);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect(int left, int top, int right, int bottom)
    {
        public readonly int Left = left;
        public readonly int Top = top;
        public readonly int Right = right;
        public readonly int Bottom = bottom;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
