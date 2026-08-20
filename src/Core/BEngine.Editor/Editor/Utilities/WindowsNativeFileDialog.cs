using System.Runtime.InteropServices;
using System.Text;

namespace BEngine.Editor;

internal static class WindowsNativeFileDialog
{
    private const uint OfnOverwritePrompt = 0x00000002;
    private const uint OfnNoChangeDirectory = 0x00000008;
    private const uint OfnPathMustExist = 0x00000800;
    private const uint OfnFileMustExist = 0x00001000;
    private const uint OfnExplorer = 0x00080000;
    private const uint BifReturnOnlyFileSystemDirectories = 0x0001;
    private const uint BifEditBox = 0x0010;
    private const uint BifNewDialogStyle = 0x0040;
    private const int BffmInitialized = 1;
    private const uint BffmSetSelectionW = 0x0467;
    private const int MaximumPathLength = 32768;

    internal static string? OpenFile(string title, string initialDirectory, string filter) =>
        ShowFileDialog(title, initialDirectory, filter, string.Empty, save: false);

    internal static string? SaveFile(
        string title,
        string initialDirectory,
        string filter,
        string defaultName) => ShowFileDialog(title, initialDirectory, filter, defaultName, save: true);

    internal static string? OpenFolder(string title, string initialDirectory)
    {
        var directory = ExistingDirectory(initialDirectory);
        var displayName = Marshal.AllocHGlobal(MaximumPathLength * sizeof(char));
        var initialized = new BrowseCallback((window, message, _, _) =>
        {
            if (message == BffmInitialized) _ = SendMessage(window, BffmSetSelectionW, new IntPtr(1), directory);
            return 0;
        });
        var browseInfo = new BrowseInfo
        {
            Owner = OwnerWindow(),
            DisplayName = displayName,
            Title = title,
            Flags = BifReturnOnlyFileSystemDirectories | BifEditBox | BifNewDialogStyle,
            Callback = initialized
        };
        var initializedCom = OleInitialize(IntPtr.Zero) >= 0;
        IntPtr item = IntPtr.Zero;
        try
        {
            item = SHBrowseForFolder(ref browseInfo);
            if (item == IntPtr.Zero) return null;
            var path = new StringBuilder(MaximumPathLength);
            return SHGetPathFromIDList(item, path) ? Path.GetFullPath(path.ToString()) : null;
        }
        finally
        {
            GC.KeepAlive(initialized);
            if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
            Marshal.FreeHGlobal(displayName);
            if (initializedCom) OleUninitialize();
        }
    }

    private static string? ShowFileDialog(
        string title,
        string initialDirectory,
        string filter,
        string defaultName,
        bool save)
    {
        var file = new StringBuilder(MaximumPathLength);
        if (!string.IsNullOrWhiteSpace(defaultName)) file.Append(defaultName);
        var options = new OpenFileName
        {
            Size = Marshal.SizeOf<OpenFileName>(),
            Owner = OwnerWindow(),
            Filter = NativeFilter(filter),
            FilterIndex = 1,
            File = file,
            MaximumFile = file.Capacity,
            InitialDirectory = ExistingDirectory(initialDirectory),
            Title = title,
            Flags = OfnExplorer | OfnPathMustExist | OfnNoChangeDirectory |
                    (save ? OfnOverwritePrompt : OfnFileMustExist),
            DefaultExtension = DefaultExtension(filter)
        };
        var accepted = save ? GetSaveFileName(ref options) : GetOpenFileName(ref options);
        if (accepted) return Path.GetFullPath(file.ToString());
        var error = CommDlgExtendedError();
        if (error != 0) throw new InvalidOperationException($"Windows file dialog failed with error 0x{error:X8}.");
        return null;
    }

    private static string ExistingDirectory(string path) => Directory.Exists(path)
        ? Path.GetFullPath(path)
        : Environment.CurrentDirectory;

    private static string NativeFilter(string filter)
    {
        var parts = filter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length % 2 != 0) return "All Files\0*.*\0\0";
        return string.Join('\0', parts) + "\0\0";
    }

    private static string? DefaultExtension(string filter)
    {
        var parts = filter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var pattern = parts[1].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return pattern?.TrimStart('*', '.');
    }

    private static IntPtr OwnerWindow()
    {
        var owner = GetActiveWindow();
        return owner != IntPtr.Zero ? owner : GetForegroundWindow();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        internal int Size;
        internal IntPtr Owner;
        internal IntPtr Instance;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Filter;
        internal IntPtr CustomFilter;
        internal int MaximumCustomFilter;
        internal int FilterIndex;
        internal StringBuilder File;
        internal int MaximumFile;
        internal IntPtr FileTitle;
        internal int MaximumFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] internal string InitialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Title;
        internal uint Flags;
        internal short FileOffset;
        internal short FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? DefaultExtension;
        internal IntPtr CustomData;
        internal IntPtr Hook;
        internal IntPtr TemplateName;
        internal IntPtr Reserved;
        internal uint ReservedSize;
        internal uint ExtendedFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        internal IntPtr Owner;
        internal IntPtr Root;
        internal IntPtr DisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Title;
        internal uint Flags;
        internal BrowseCallback Callback;
        internal IntPtr Parameter;
        internal int Image;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BrowseCallback(IntPtr window, int message, IntPtr parameter, IntPtr data);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName value);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileName value);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BrowseInfo value);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDList(IntPtr item, StringBuilder path);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, string data);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();
}
