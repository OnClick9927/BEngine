namespace BEngine.Editor;

internal static partial class NativeDpiAwareness
{
    public static void EnablePerMonitorV2()
    {
        if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
        try { SetProcessDpiAwareness(2); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(IntPtr value);

    [System.Runtime.InteropServices.LibraryImport("shcore.dll")]
    private static partial int SetProcessDpiAwareness(int awareness);
}
