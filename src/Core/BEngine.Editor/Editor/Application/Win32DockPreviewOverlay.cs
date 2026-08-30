using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BEngine.Editor;

/// <summary>A non-activating, mouse-transparent screen-space dock target shown during Win32 moves.</summary>
internal sealed class Win32DockPreviewOverlay : Form
{
    private const int WindowHitTest = 0x0084;
    private const int HitTransparent = -1;
    private const int ExtendedToolWindow = 0x00000080;
    private const int ExtendedTransparent = 0x00000020;
    private const int ExtendedNoActivate = 0x08000000;
    private const uint NoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly System.Drawing.Color PreviewColor =
        System.Drawing.Color.FromArgb(50, 140, 230);

    internal Win32DockPreviewOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = PreviewColor;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Opacity = 0.34;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= ExtendedToolWindow | ExtendedTransparent | ExtendedNoActivate;
            return parameters;
        }
    }

    internal void ShowPreview(Rect screenRect)
    {
        if (!OperatingSystem.IsWindows()) return;
        var left = (int)Math.Floor((double)screenRect.x);
        var top = (int)Math.Floor((double)screenRect.y);
        var right = (int)Math.Ceiling((double)screenRect.xMax);
        var bottom = (int)Math.Ceiling((double)screenRect.yMax);
        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);
        if (!Visible)
        {
            SetBounds(left, top, width, height, BoundsSpecified.All);
            base.Show();
        }
        _ = SetWindowPos(Handle, HwndTopMost, left, top, width, height,
            NoActivate | ShowWindow);
        Invalidate();
    }

    internal void HidePreview()
    {
        if (Visible) Hide();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        args.Graphics.Clear(PreviewColor);
        using var pen = new Pen(System.Drawing.Color.FromArgb(35, 120, 220), 5);
        args.Graphics.DrawRectangle(pen, 2, 2, Math.Max(0, ClientSize.Width - 5),
            Math.Max(0, ClientSize.Height - 5));
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WindowHitTest)
        {
            message.Result = new IntPtr(HitTransparent);
            return;
        }
        base.WndProc(ref message);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
