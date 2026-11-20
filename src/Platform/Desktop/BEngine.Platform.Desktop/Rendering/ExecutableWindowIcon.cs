using System.Runtime.InteropServices;
using Silk.NET.Windowing;
namespace BEngine.Rendering;
internal static class ExecutableWindowIcon
{
    internal static bool TryApply(IWindow window)
    {
        if (!OperatingSystem.IsWindows() || Application.isEditor || window.Native?.Win32?.Hwnd is not { } handle || handle == IntPtr.Zero) return false;
        var file = Environment.ProcessPath;
        if (file is null || Path.GetFileNameWithoutExtension(file).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return false;
        if (ExtractIconExW(file, 0, out var large, out var small, 1) == 0) return false;
        SendMessageW(handle, 0x80, (IntPtr)1, large); SendMessageW(handle, 0x80, IntPtr.Zero, small);
        window.Closing += () => { if (large != IntPtr.Zero) DestroyIcon(large); if (small != IntPtr.Zero && small != large) DestroyIcon(small); };
        return true;
    }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr kind, IntPtr icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
