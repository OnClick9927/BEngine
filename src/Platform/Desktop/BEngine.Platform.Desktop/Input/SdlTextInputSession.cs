using System.Runtime.InteropServices;
using Silk.NET.SDL;
using Silk.NET.Maths;

namespace BEngine.Platform;

/// <summary>Native SDL composition for Cocoa, X11 and Wayland input methods.</summary>
public sealed unsafe class SdlTextInputSession : ITextInputService, IDisposable
{
    private readonly Sdl _sdl = Sdl.GetApi();
    private readonly PfnEventFilter _watch;
    private readonly uint _id;
    private readonly IntPtr _window;
    private volatile string _composition = "";
    private bool _active, _disposed;
    public SdlTextInputSession(IntPtr window)
    {
        if (window == IntPtr.Zero) throw new ArgumentException("An initialized SDL window is required.", nameof(window));
        _window = window; _id = _sdl.GetWindowID((Silk.NET.SDL.Window*)window);
        _sdl.SetHint("SDL_IME_SUPPORT_EXTENDED_TEXT", "1");
        _watch = new PfnEventFilter(Observe);
        _sdl.AddEventWatch(_watch, null);
    }
    public void SetCaret(IntPtr window, Rect? caret)
    {
        if (_disposed || window != _window) return;
        var focused = _sdl.GetKeyboardFocus() == (Silk.NET.SDL.Window*)_window;
        var active = focused && caret is not null;
        if (active != _active)
        {
            _active = active;
            if (active) _sdl.StartTextInput();
            else { _sdl.StopTextInput(); _composition = ""; }
        }
        if (active && caret is { } rect)
        {
            var native = new Rectangle<int>((int)rect.x, (int)rect.y, Math.Max(1, (int)rect.width), Math.Max(1, (int)rect.height));
            _sdl.SetTextInputRect(ref native);
        }
    }
    public bool IsComposing(IntPtr window) => !_disposed && window == _window && _composition.Length > 0;
    public string GetComposition(IntPtr window) => !_disposed && window == _window ? _composition : "";
    private int Observe(void* user, Silk.NET.SDL.Event* value)
    {
        // Watches observe the queue; Silk's input backend retains ownership of committed text.
        if (_disposed || !_active || value == null) return 1;
        if (value->Type == (uint)Silk.NET.SDL.EventType.Textediting && value->Edit.WindowID == _id)
            _composition = Marshal.PtrToStringUTF8((IntPtr)value->Edit.Text) ?? "";
        else if (value->Type == 0x305 && ((ExtendedEditing*)value)->WindowId == _id)
            _composition = Marshal.PtrToStringUTF8(((ExtendedEditing*)value)->Text) ?? "";
        else if (value->Type == (uint)Silk.NET.SDL.EventType.Textinput && value->Text.WindowID == _id)
            _composition = "";
        else if (value->Type == (uint)Silk.NET.SDL.EventType.Windowevent && value->Window.WindowID == _id && value->Window.Event == (byte)WindowEventID.FocusLost)
            _composition = "";
        return 1;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedEditing { internal uint Type, Timestamp, WindowId; internal IntPtr Text; internal int Start, Length; }
    public void Dispose()
    {
        if (_disposed) return;
        SetCaret(_window, null); _disposed = true; _sdl.DelEventWatch(_watch, null); _watch.Dispose(); _composition = ""; _sdl.Dispose();
    }
}
