namespace BEngine.Editor;

internal static partial class NativeStartupDialog
{
    public static void ShowError(string title, string message) =>
        MessageBox(IntPtr.Zero, message, title, 0x00000010);

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "MessageBoxW",
        StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr owner, string text, string caption, uint type);
}
