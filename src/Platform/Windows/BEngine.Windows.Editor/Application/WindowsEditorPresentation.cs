namespace BEngine.Editor;

/// <summary>Creates native Windows presentation services without exposing window implementation types.</summary>
public static class WindowsEditorPresentation
{
    /// <summary>Sets the editor shell identity before creating native windows.</summary>
    public static void InitializeProcess() => WindowsEditorTaskbar.InitializeProcess();

    /// <summary>Enables per-monitor DPI handling before creating native windows.</summary>
    public static void EnablePerMonitorDpi() => NativeDpiAwareness.EnablePerMonitorV2();

    public static void ShowStartupError(string title, string message) => NativeStartupDialog.ShowError(title, message);

    public static bool TryShowContextMenu(
        IntPtr owner,
        Vector2 screenPosition,
        IReadOnlyList<GenericMenuItem> items,
        Rect? keepAliveScreenRect = null) =>
        Win32GenericMenuPresenter.TryShow(owner, screenPosition, items, keepAliveScreenRect);

    /// <summary>Create and dispose on the editor window thread.</summary>
    public static IEditorDockPreview CreateDockPreview() => new Win32DockPreviewOverlay();

    /// <summary>Returns null when a native menu cannot be attached to the supplied window.</summary>
    public static IEditorNativeMainMenu? TryCreateMainMenu(
        IntPtr owner,
        IReadOnlyList<string> roots,
        Func<string, IReadOnlyList<GenericMenuItem>> itemsForRoot) =>
        Win32MainMenuBar.TryCreate(owner, roots, itemsForRoot);
}
