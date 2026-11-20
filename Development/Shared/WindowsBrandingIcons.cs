using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BEngine.Tools;

/// <summary>Keep the caption legible without changing the white shell/taskbar mark.</summary>
internal sealed class WindowsBrandingIcons : System.Windows.Forms.NativeWindow, IDisposable
{
    private readonly Icon _darkCaption;
    private readonly Icon _lightCaption;
    private readonly Icon _taskbar;
    private IntPtr _caption;
    private bool _disposed;

    internal WindowsBrandingIcons(IntPtr window, string resourcePrefix)
    {
        _darkCaption = Load(resourcePrefix + ".BEngine.Dark.ico", 49, 50);
        _lightCaption = Load(resourcePrefix + ".BEngine.ico", 49, 50);
        _taskbar = Load(resourcePrefix + ".BEngine.ico", 11, 12);
        AssignHandle(window);
        Apply();
    }

    private static Icon Load(string resource, int widthMetric, int heightMetric)
    {
        using var stream = typeof(WindowsBrandingIcons).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded window icon '{resource}' is missing.");
        using var icon = new Icon(stream, GetSystemMetrics(widthMetric), GetSystemMetrics(heightMetric));
        return (Icon)icon.Clone();
    }

    private void Apply()
    {
        if (Handle == IntPtr.Zero) return;
        var caption = UsesLightCaptionInk() ? _lightCaption.Handle : _darkCaption.Handle;
        if (_caption == caption) return;
        _caption = caption;
        SendMessage(Handle, 0x0080, IntPtr.Zero, caption); // ICON_SMALL: native caption
        SendMessage(Handle, 0x0080, new IntPtr(1), _taskbar.Handle); // ICON_BIG: taskbar / Alt+Tab
    }

    private bool UsesLightCaptionInk()
    {
        if (SystemInformation.HighContrast)
            return IsLight(GetSysColor(9)); // COLOR_CAPTIONTEXT
        // Native Win32 captions default to light even when the application's
        // client area and Windows taskbar are dark. Query the window, not our UI theme.
        if (DwmGetWindowAttribute(Handle, 36, out var text, sizeof(uint)) == 0 && text < 0xfffffffe)
            return IsLight(text);
        if (DwmGetWindowAttribute(Handle, 35, out var background, sizeof(uint)) == 0 && background < 0xfffffffe)
            return !IsLight(background);
        return DwmGetWindowAttribute(Handle, 20, out var dark, sizeof(uint)) == 0 && dark != 0;
    }

    private static bool IsLight(uint color) =>
        (color & 255) * 2126 + ((color >> 8) & 255) * 7152 + ((color >> 16) & 255) * 722 >= 128 * 10000;

    protected override void WndProc(ref Message message)
    {
        // WinForms reapplies Form.Icon after HandleCreated and on DPI changes.
        // Keep those native updates consistent with the separate caption/taskbar ink.
        if (!_disposed && message.Msg == 0x0080 && _caption != IntPtr.Zero)
            message.LParam = message.WParam == new IntPtr(1) ? _taskbar.Handle : _caption;
        base.WndProc(ref message);
        // Activation, system colors/settings, theme and DWM color changes. No polling.
        if (message.Msg is 0x0006 or 0x0015 or 0x001a or 0x031a or 0x0320) Apply();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Restore the class/Form icons before freeing any handles still used by Windows.
        if (Handle != IntPtr.Zero)
        {
            SendMessage(Handle, 0x0080, IntPtr.Zero, IntPtr.Zero);
            SendMessage(Handle, 0x0080, new IntPtr(1), IntPtr.Zero);
            ReleaseHandle();
        }
        _darkCaption.Dispose();
        _lightCaption.Dispose();
        _taskbar.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetSysColor(int index);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out uint value, int size);
}
