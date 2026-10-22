using System.Runtime.InteropServices;

namespace BEngine.Platform;

public sealed class WindowsTextInputService : ITextInputService
{
    private IntPtr _caretWindow;
    private int _caretHeight;
    private readonly Dictionary<IntPtr, IntPtr> _disabledContexts = [];

    public void SetCaret(IntPtr window, Rect? caret)
    {
        if (window == IntPtr.Zero) return;
        if (caret is null)
        {
            if (!_disabledContexts.ContainsKey(window))
            {
                var inactiveContext = ImmGetContext(window);
                if (inactiveContext != IntPtr.Zero)
                {
                    ImmNotifyIME(inactiveContext, 0x15, 4, 0);
                    ImmReleaseContext(window, inactiveContext);
                    _disabledContexts[window] = ImmAssociateContext(window, IntPtr.Zero);
                }
            }
        }
        else if (_disabledContexts.Remove(window, out var previous)) ImmAssociateContext(window, previous);
        if (caret is not { } rect || GetFocus() != window)
        {
            if (_caretWindow == window) { DestroyCaret(); _caretWindow = IntPtr.Zero; }
            return;
        }
        var x = Math.Max(0, (int)rect.x);
        var y = Math.Max(0, (int)rect.y);
        var height = Math.Max(1, (int)rect.height);
        if (_caretWindow != window || _caretHeight != height)
        {
            if (_caretWindow != IntPtr.Zero) DestroyCaret();
            _caretWindow = CreateCaret(window, IntPtr.Zero, 1, height) ? window : IntPtr.Zero;
            _caretHeight = height;
        }
        // The invisible OS caret also anchors TSF-based input methods.
        SetCaretPos(x, y);
        var context = ImmGetContext(window);
        if (context == IntPtr.Zero) return;
        try
        {
            var composition = new CompositionForm { Style = 2, Position = new Point(x, y) };
            ImmSetCompositionWindow(context, ref composition);
            var candidate = new CandidateForm
            {
                Style = 0x80, Position = new Point(x, y + height),
                Area = new NativeRect { Left = x, Top = y, Right = x + 1, Bottom = y + height }
            };
            ImmSetCandidateWindow(context, ref candidate);
        }
        finally { ImmReleaseContext(window, context); }
    }

    public void ReleaseWindow(IntPtr window)
    {
        if (_caretWindow == window) { DestroyCaret(); _caretWindow = IntPtr.Zero; }
        if (_disabledContexts.Remove(window, out var previous)) ImmAssociateContext(window, previous);
    }

    public bool IsComposing(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        var context = ImmGetContext(window);
        if (context == IntPtr.Zero) return false;
        try { return ImmGetCompositionStringW(context, 8, IntPtr.Zero, 0) > 0; }
        finally { ImmReleaseContext(window, context); }
    }
    public string GetComposition(IntPtr window)
    {
        if (window == IntPtr.Zero) return string.Empty;
        var context = ImmGetContext(window); if (context == IntPtr.Zero) return string.Empty;
        try
        {
            var length = ImmGetCompositionStringW(context, 8, IntPtr.Zero, 0);
            if (length <= 0 || length > 65536) return string.Empty;
            var buffer = Marshal.AllocHGlobal(length);
            try { var read = ImmGetCompositionStringW(context, 8, buffer, (uint)length); return read > 0 ? Marshal.PtrToStringUni(buffer, read / 2) ?? string.Empty : string.Empty; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { ImmReleaseContext(window, context); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionForm { public uint Style; public Point Position; public NativeRect Area; }
    [StructLayout(LayoutKind.Sequential)] private struct CandidateForm { public uint Index, Style; public Point Position; public NativeRect Area; }
    [DllImport("imm32.dll")] private static extern IntPtr ImmAssociateContext(IntPtr window, IntPtr context);
    [DllImport("imm32.dll")] private static extern bool ImmNotifyIME(IntPtr context, uint action, uint index, uint value);
    [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
    [DllImport("imm32.dll")] private static extern bool ImmSetCompositionWindow(IntPtr context, ref CompositionForm form);
    [DllImport("imm32.dll")] private static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)] private static extern int ImmGetCompositionStringW(IntPtr context, uint index, IntPtr buffer, uint length);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern bool CreateCaret(IntPtr window, IntPtr bitmap, int width, int height);
    [DllImport("user32.dll")] private static extern bool DestroyCaret();
    [DllImport("user32.dll")] private static extern bool SetCaretPos(int x, int y);
}

