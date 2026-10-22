using System.Runtime.InteropServices;

namespace BEngine.Editor;

/// <summary>Projects GenericMenu items onto a native Win32 popup menu when an editor HWND exists.</summary>
internal static partial class Win32GenericMenuPresenter
{
    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfChecked = 0x0008;
    private const uint MfPopup = 0x0010;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCommand = 0x0100;
    private const uint WmNull = 0x0000;

    internal static bool TryShow(
        IntPtr owner,
        Vector2 screenPosition,
        IReadOnlyList<GenericMenuItem> items,
        Rect? keepAliveScreenRect = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!OperatingSystem.IsWindows() || owner == IntPtr.Zero) return false;

        var roots = BuildNodes(items);
        if (roots.Count == 0) return false;

        IntPtr menu = IntPtr.Zero;
        try
        {
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return false;

            var commands = new Dictionary<uint, NativeMenuCommand>();
            uint nextCommandId = 1;
            if (!AppendNodes(menu, roots, commands, ref nextCommandId)) return false;

            // TrackPopupMenuEx requires a foreground owner to dismiss reliably when the user
            // clicks another editor window. WM_NULL completes the documented foreground handoff.
            _ = SetForegroundWindow(owner);
            using var mouseExit = Win32MenuMouseExitScope.TryInstall(
                keepAliveScreenRect is { } anchor
                    ? NativeScreenRect.From(anchor)
                    : NativeScreenRect.Around(screenPosition));
            var selected = TrackPopupMenuEx(menu,
                TpmRightButton | TpmNoNotify | TpmReturnCommand,
                (int)screenPosition.x, (int)screenPosition.y, owner, IntPtr.Zero);
            _ = PostMessage(owner, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (selected != 0 && commands.TryGetValue(selected, out var command))
                EditorFeatureGuard.Invoke($"GenericMenu {command.Name}", command.Action);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero) _ = DestroyMenu(menu);
        }
    }

    internal static IReadOnlyList<NativeMenuNode> BuildNodes(
        IReadOnlyList<GenericMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var roots = new List<NativeMenuNode>();
        foreach (var item in items)
        {
            var segments = item.Path.Replace('\\', '/').Split('/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var level = roots;
            if (item.Separator)
            {
                foreach (var segment in segments)
                    level = FindOrAdd(level, segment).Children;
                level.Add(NativeMenuNode.CreateSeparator());
                continue;
            }

            if (segments.Length == 0) continue;
            for (var index = 0; index < segments.Length - 1; index++)
                level = FindOrAdd(level, segments[index]).Children;

            // Win32 permits repeated labels. Keep leaves distinct so histories with repeated
            // operation names do not overwrite each other's command callbacks.
            var leaf = new NativeMenuNode(segments[^1]);
            level.Add(leaf);

            leaf.On = item.On;
            leaf.Enabled = item.Enabled;
            leaf.Action = item.Action;
        }
        return roots;
    }

    private static bool AppendNodes(
        IntPtr menu,
        IReadOnlyList<NativeMenuNode> nodes,
        Dictionary<uint, NativeMenuCommand> commands,
        ref uint nextCommandId)
    {
        foreach (var node in nodes)
        {
            if (node.Separator)
            {
                if (!AppendMenu(menu, MfSeparator, 0, null)) return false;
                continue;
            }

            var flags = MfString |
                        (node.On ? MfChecked : 0) |
                        (!node.IsEnabled ? MfGrayed : 0);
            var label = node.Name.Replace("&", "&&", StringComparison.Ordinal);
            if (node.Children.Count > 0)
            {
                var submenu = CreatePopupMenu();
                if (submenu == IntPtr.Zero) return false;
                if (!AppendNodes(submenu, node.Children, commands, ref nextCommandId) ||
                    !AppendMenu(menu, flags | MfPopup, (nuint)submenu, label))
                {
                    _ = DestroyMenu(submenu);
                    return false;
                }
                continue;
            }

            uint commandId = 0;
            if (node.IsEnabled && node.Action is not null)
            {
                commandId = nextCommandId++;
                commands.Add(commandId, new NativeMenuCommand(node.Name, node.Action));
            }
            if (!AppendMenu(menu, flags, commandId, label)) return false;
        }
        return true;
    }

    private static NativeMenuNode FindOrAdd(List<NativeMenuNode> nodes, string name)
    {
        var node = nodes.FirstOrDefault(candidate => !candidate.Separator && candidate.Name == name);
        if (node is not null) return node;
        node = new NativeMenuNode(name);
        nodes.Add(node);
        return node;
    }

    internal sealed class NativeMenuNode(string name)
    {
        internal string Name { get; } = name;
        internal List<NativeMenuNode> Children { get; } = [];
        internal bool On { get; set; }
        internal bool Enabled { get; set; } = true;
        internal bool Separator { get; private init; }
        internal Action? Action { get; set; }
        internal bool IsEnabled => !Separator &&
                                   (Children.Count == 0
                                       ? Enabled && Action is not null
                                       : Children.Any(child => child.IsEnabled));

        internal static NativeMenuNode CreateSeparator() => new(string.Empty)
        {
            Separator = true,
            Enabled = false
        };
    }

    private readonly record struct NativeMenuCommand(string Name, Action Action);

    [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "DestroyMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenu(IntPtr menu, uint flags, nuint item, string? text);

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenuEx")]
    private static partial uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr owner,
        IntPtr parameters);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
