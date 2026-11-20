using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BEngine.Platform;

/// <summary>WM_TOUCH input, owned by the window thread. Mouse promotion is filtered only for this window.</summary>
public sealed class WindowsTouchInputService : ITouchInputService
{
    public ITouchInputSource? Attach(IntPtr window, bool suppressEmulatedMouse = true) =>
        window == IntPtr.Zero ? null : new WindowTouchSource(window, suppressEmulatedMouse);

    private sealed class WindowTouchSource : ITouchInputSource
    {
        // Native callbacks are not GC roots. Keep the source/delegate alive until removal or WM_NCDESTROY.
        private static readonly Dictionary<IntPtr, WindowTouchSource> Attached = [];
        private readonly IntPtr _window;
        private readonly uint _thread;
        private readonly SubclassProc _callback;
        private readonly bool _suppressMouse;
        private readonly WindowsTouchBuffer _buffer = new();
        private bool _attached;
        private bool _disposed;
        private const nuint SubclassId = 0x42455443;

        internal WindowTouchSource(IntPtr window, bool suppressMouse)
        {
            _window = window; _thread = GetCurrentThreadId(); _callback = OnMessage; _suppressMouse = suppressMouse;
            if (GetWindowThreadProcessId(window, out _) != _thread)
                throw new InvalidOperationException("Touch input must be attached on the window's owner thread.");
            lock (Attached)
            {
                if (Attached.ContainsKey(window)) throw new InvalidOperationException("Touch input is already attached to this window.");
                // Noncoalesced, low-latency input; palm reports are filtered explicitly below.
                if (!RegisterTouchWindow(window, 3)) throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterTouchWindow failed.");
                if (!SetWindowSubclass(window, _callback, SubclassId, 0))
                { UnregisterTouchWindow(window); throw new InvalidOperationException("Cannot install the touch window callback."); }
                _attached = true; Attached.Add(window, this);
            }
        }

        public Touch[] Poll(int framebufferWidth, int framebufferHeight, Fix64 deltaTime)
        {
            EnsureThread();
            if (_disposed) return [];
            return _buffer.Poll(framebufferWidth, framebufferHeight, deltaTime);
        }

        public void Dispose()
        {
            if (_disposed) return;
            EnsureThread(); Detach(); _disposed = true; _buffer.Clear();
        }

        private void EnsureThread()
        {
            if (GetCurrentThreadId() != _thread)
                throw new InvalidOperationException("Touch input must be polled/disposed on the window's owner thread.");
        }

        private void Detach(bool destroying = false)
        {
            if (!_attached) return;
            if (!RemoveWindowSubclass(_window, _callback, SubclassId) && !destroying)
                throw new InvalidOperationException("Cannot remove the touch window callback.");
            UnregisterTouchWindow(_window); _attached = false;
            lock (Attached) Attached.Remove(_window);
        }

        private IntPtr OnMessage(IntPtr window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
        {
            if (message == 0x02c8) return (IntPtr)0x10019; // Disable press-and-hold, pen feedback and flick gestures.
            // WM_TOUCH owns a native handle. Close it exactly once and never forward that closed handle.
            if (message == 0x0240)
            {
                try
                {
                    var count = (int)(wParam & 0xffff);
                    if (count is > 0 and <= 128)
                    {
                        var input = new NativeTouch[count];
                        if (GetTouchInputInfo(lParam, count, input, Marshal.SizeOf<NativeTouch>()) &&
                            GetClientRect(window, out var client))
                        {
                            var origin = new NativePoint();
                            if (!ClientToScreen(window, ref origin)) { _buffer.Cancel(); return IntPtr.Zero; }
                            var width = Math.Max(1, client.Right - client.Left);
                            var height = Math.Max(1, client.Bottom - client.Top);
                            var events = new List<WindowsTouchEvent>(count);
                            foreach (var touch in input)
                            {
                                if ((touch.Flags & 0x80) != 0) continue; // Palm contacts are not game input.
                                var phase = (touch.Flags & 4) != 0 ? TouchPhase.Ended :
                                    (touch.Flags & 2) != 0 ? TouchPhase.Began : TouchPhase.Moved;
                                if ((touch.Flags & 7) == 0) continue; // Ignore hover-only reports.
                                var x = ((Fix64)touch.X / 100 - origin.X) / width;
                                var y = 1 - ((Fix64)touch.Y / 100 - origin.Y) / height;
                                var radius = (touch.Mask & 4) != 0 ?
                                    new Vector2((Fix64)touch.Width / 200 / width, (Fix64)touch.Height / 200 / height) : Vector2.zero;
                                events.Add(new(new(touch.Source, touch.Id), new(x, y), phase, radius));
                            }
                            _buffer.Enqueue(events.ToArray());
                        }
                        else _buffer.Cancel();
                    }
                    else _buffer.Cancel();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { _buffer.Cancel(); Debug.LogException(exception); }
                finally { CloseTouchInputHandle(lParam); }
                return IntPtr.Zero;
            }
            if (message is 0x0008 or 0x001f or 0x007e || message == 0x0006 && (wParam & 0xffff) == 0)
                _buffer.Cancel(); // Focus loss, cancel mode, display change, or deactivation.
            if (message == 0x0082) // WM_NCDESTROY: the host may destroy the HWND before managed disposal.
            {
                _buffer.Cancel(); Detach(destroying: true);
            }
            if (_suppressMouse && message is >= 0x0200 and <= 0x020e &&
                ((nuint)GetMessageExtraInfo() & 0xffffff00u) == 0xff515700u)
                return IntPtr.Zero;
            return DefSubclassProc(window, message, wParam, lParam);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTouch
    {
        public int X, Y;
        public IntPtr Source;
        public uint Id, Flags, Mask, Time;
        public nuint ExtraInfo;
        public uint Width, Height;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterTouchWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterTouchWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTouchInputInfo(IntPtr handle, int count, [Out] NativeTouch[] input, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseTouchInputHandle(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, nuint wParam, nint lParam);
}
