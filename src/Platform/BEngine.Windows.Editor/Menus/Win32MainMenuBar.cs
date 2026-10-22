using System.Runtime.InteropServices;

namespace BEngine.Editor;

/// <summary>Owns the real Win32 menu bar attached to the editor's main HWND.</summary>
internal sealed partial class Win32MainMenuBar : IDisposable
{
    private const int WindowProcedureIndex = -4;
    private const uint WmCommand = 0x0111;
    private const uint WmInitMenuPopup = 0x0117;
    private const uint WmEnterMenuLoop = 0x0211;
    private const uint WmExitMenuLoop = 0x0212;
    private const uint WmNcDestroy = 0x0082;
    private const uint MfByPosition = 0x0400;
    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfChecked = 0x0008;
    private const uint MfPopup = 0x0010;
    private const uint MfSeparator = 0x0800;
    private const int ObjectIdMenu = -3;

    private readonly IntPtr _owner;
    private readonly Func<string, IReadOnlyList<GenericMenuItem>> _itemsForRoot;
    private readonly WindowProcedure _windowProcedure;
    private readonly Dictionary<IntPtr, string> _rootMenus = [];
    private readonly Dictionary<uint, NativeMenuCommand> _commands = [];
    private string[] _roots = [];
    private IntPtr _menu;
    private IntPtr _previousWindowProcedure;
    private Win32MenuMouseExitScope? _mouseExit;
    private bool _disposed;

    private Win32MainMenuBar(
        IntPtr owner,
        Func<string, IReadOnlyList<GenericMenuItem>> itemsForRoot)
    {
        _owner = owner;
        _itemsForRoot = itemsForRoot;
        _windowProcedure = WindowMessage;
    }

    internal bool isInstalled => !_disposed && _menu != IntPtr.Zero;

    internal static Win32MainMenuBar? TryCreate(
        IntPtr owner,
        IReadOnlyList<string> roots,
        Func<string, IReadOnlyList<GenericMenuItem>> itemsForRoot)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(itemsForRoot);
        if (!OperatingSystem.IsWindows() || owner == IntPtr.Zero) return null;

        var menuBar = new Win32MainMenuBar(owner, itemsForRoot);
        try
        {
            menuBar._previousWindowProcedure = GetWindowLongPtr(owner, WindowProcedureIndex);
            if (menuBar._previousWindowProcedure == IntPtr.Zero) return null;
            var procedure = Marshal.GetFunctionPointerForDelegate(menuBar._windowProcedure);
            if (SetWindowLongPtr(owner, WindowProcedureIndex, procedure) == IntPtr.Zero)
                return null;
            if (!menuBar.SynchronizeRoots(roots))
            {
                menuBar.Dispose();
                return null;
            }
            return menuBar;
        }
        catch (DllNotFoundException)
        {
            menuBar.Dispose();
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            menuBar.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            menuBar.Dispose();
            EditorFeatureGuard.Report("Install Win32 main menu bar", exception);
            return null;
        }
    }

    internal bool SynchronizeRoots(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (_disposed) return false;
        var normalized = NormalizeRoots(roots);
        if (_menu != IntPtr.Zero && _roots.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
            return true;

        var replacement = CreateMenu();
        if (replacement == IntPtr.Zero) return false;
        var replacementRoots = new Dictionary<IntPtr, string>();
        foreach (var root in normalized)
        {
            var popup = CreatePopupMenu();
            if (popup == IntPtr.Zero ||
                !AppendMenu(popup, MfString | MfGrayed, 0, "(Loading...)") ||
                !AppendMenu(replacement, MfString | MfPopup, (nuint)popup, EscapeLabel(root)))
            {
                if (popup != IntPtr.Zero) _ = DestroyMenu(popup);
                _ = DestroyMenu(replacement);
                return false;
            }
            replacementRoots.Add(popup, root);
        }

        var previous = _menu;
        if (!SetMenu(_owner, replacement))
        {
            _ = DestroyMenu(replacement);
            return false;
        }

        _menu = replacement;
        _roots = normalized;
        _rootMenus.Clear();
        foreach (var pair in replacementRoots) _rootMenus.Add(pair.Key, pair.Value);
        _commands.Clear();
        _ = DrawMenuBar(_owner);
        if (previous != IntPtr.Zero) _ = DestroyMenu(previous);
        return true;
    }

    internal static string[] NormalizeRoots(IReadOnlyList<string> roots) =>
        roots.Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EndMouseExitTracking();
        if (_previousWindowProcedure != IntPtr.Zero && IsWindow(_owner))
        {
            _ = SetWindowLongPtr(_owner, WindowProcedureIndex, _previousWindowProcedure);
            _previousWindowProcedure = IntPtr.Zero;
            _ = SetMenu(_owner, IntPtr.Zero);
            _ = DrawMenuBar(_owner);
        }
        if (_menu != IntPtr.Zero)
        {
            _ = DestroyMenu(_menu);
            _menu = IntPtr.Zero;
        }
        _rootMenus.Clear();
        _commands.Clear();
        GC.KeepAlive(_windowProcedure);
    }

    private IntPtr WindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WmInitMenuPopup when _rootMenus.TryGetValue(wParam, out var root):
                    PopulateRoot(wParam, root);
                    break;
                case WmEnterMenuLoop:
                    BeginMouseExitTracking();
                    break;
                case WmExitMenuLoop:
                    EndMouseExitTracking();
                    break;
                case WmCommand when lParam == IntPtr.Zero:
                {
                    var id = unchecked((uint)((nuint)wParam & 0xffff));
                    if (_commands.TryGetValue(id, out var command))
                    {
                        EditorFeatureGuard.Invoke($"Main menu {command.Name}", command.Action);
                        return IntPtr.Zero;
                    }
                    break;
                }
                case WmNcDestroy:
                    EndMouseExitTracking();
                    break;
            }
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report("Win32 main menu window procedure", exception);
        }
        return CallWindowProc(_previousWindowProcedure, window, message, wParam, lParam);
    }

    private void PopulateRoot(IntPtr popup, string root)
    {
        var count = GetMenuItemCount(popup);
        for (var index = count - 1; index >= 0; index--)
            _ = DeleteMenu(popup, (uint)index, MfByPosition);
        _commands.Clear();

        IReadOnlyList<GenericMenuItem> items = [];
        EditorFeatureGuard.TryInvoke($"Build main menu {root}", () => _itemsForRoot(root), [], out items);
        var nodes = Win32GenericMenuPresenter.BuildNodes(items);
        uint nextCommandId = 1;
        AppendNodes(popup, nodes, ref nextCommandId);
        if (GetMenuItemCount(popup) == 0)
            _ = AppendMenu(popup, MfString | MfGrayed, 0, "(Empty)");
    }

    private void AppendNodes(IntPtr menu, IReadOnlyList<Win32GenericMenuPresenter.NativeMenuNode> nodes,
        ref uint nextCommandId)
    {
        foreach (var node in nodes)
        {
            if (node.Separator)
            {
                _ = AppendMenu(menu, MfSeparator, 0, null);
                continue;
            }

            var flags = MfString | (node.On ? MfChecked : 0) | (!node.IsEnabled ? MfGrayed : 0);
            if (node.Children.Count > 0)
            {
                var submenu = CreatePopupMenu();
                if (submenu == IntPtr.Zero) continue;
                AppendNodes(submenu, node.Children, ref nextCommandId);
                if (!AppendMenu(menu, flags | MfPopup, (nuint)submenu, EscapeLabel(node.Name)))
                    _ = DestroyMenu(submenu);
                continue;
            }

            uint commandId = 0;
            if (node.IsEnabled && node.Action is not null)
            {
                commandId = nextCommandId++;
                _commands[commandId] = new NativeMenuCommand(node.Name, node.Action);
            }
            _ = AppendMenu(menu, flags, commandId, EscapeLabel(node.Name));
        }
    }

    private void BeginMouseExitTracking()
    {
        EndMouseExitTracking();
        _mouseExit = Win32MenuMouseExitScope.TryInstall(GetMenuBarRect());
    }

    private void EndMouseExitTracking()
    {
        _mouseExit?.Dispose();
        _mouseExit = null;
    }

    private NativeScreenRect? GetMenuBarRect()
    {
        var information = new NativeMenuBarInfo { Size = (uint)Marshal.SizeOf<NativeMenuBarInfo>() };
        return GetMenuBarInfo(_owner, ObjectIdMenu, 0, ref information)
            ? new NativeScreenRect(information.Bounds.Left, information.Bounds.Top,
                information.Bounds.Right, information.Bounds.Bottom)
            : null;
    }

    private static string EscapeLabel(string value) => value.Replace("&", "&&", StringComparison.Ordinal);

    private readonly record struct NativeMenuCommand(string Name, Action Action);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMenuBarInfo
    {
        internal uint Size;
        internal NativeRectangle Bounds;
        internal IntPtr Menu;
        internal IntPtr MenuWindow;
        internal uint Flags;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateMenu")]
    private static partial IntPtr CreateMenu();

    [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "DestroyMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenu(IntPtr menu, uint flags, nuint item, string? text);

    [LibraryImport("user32.dll", EntryPoint = "DeleteMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteMenu(IntPtr menu, uint position, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuItemCount")]
    private static partial int GetMenuItemCount(IntPtr menu);

    [LibraryImport("user32.dll", EntryPoint = "SetMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenu(IntPtr window, IntPtr menu);

    [LibraryImport("user32.dll", EntryPoint = "DrawMenuBar")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawMenuBar(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuBarInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMenuBarInfo(
        IntPtr window,
        int objectId,
        int itemId,
        ref NativeMenuBarInfo information);

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static partial IntPtr CallWindowProc(
        IntPtr previousProcedure,
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
