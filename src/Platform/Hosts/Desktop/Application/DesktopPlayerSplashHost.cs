using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BEngine.Player;

namespace BEngine.Player.Hosts.Desktop;

public sealed class DesktopPlayerSplashHost : IPlayerSplashHost
{
    private IPlayerSplashWindow? _window;

    public IPlayerSplashWindow? Show(PlayerSplashPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return _window ??= OperatingSystem.IsWindows() ? WindowsSplashWindow.Start(presentation) : null;
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsSplashWindow : IPlayerSplashWindow
    {
        private const uint WsPopup = 0x80000000;
        private const uint WsExTopmost = 0x00000008;
        private const uint WsExToolWindow = 0x00000080;
        private const int SwShow = 5;
        private const uint WmNccreate = 0x0081;
        private const uint WmNcdestroy = 0x0082;
        private const uint WmPaint = 0x000f;
        private const uint WmClose = 0x0010;
        private const uint WmDestroy = 0x0002;
        private const int GwlpUserData = -21;
        private const int SmCxscreen = 0;
        private const int SmCyscreen = 1;
        private const uint DibRgbColors = 0;
        private const uint Srccopy = 0x00cc0020;
        private const uint BiRgb = 0;
        private const uint CsHredraw = 0x0002;
        private const uint CsVredraw = 0x0001;
        private const uint PmRemove = 0x0001;
        private static readonly WindowProc SharedWindowProcedure = DispatchWindowMessage;

        private readonly string _className = $"BEngine.Player.Splash.{Guid.NewGuid():N}";
        private readonly string _title;
        private readonly int _imageWidth;
        private readonly int _imageHeight;
        private readonly byte[] _pixels;
        private readonly uint _background;
        private GCHandle _ownerHandle;
        private nint _windowHandle;
        private ushort _windowClass;
        private volatile bool _disposed;
        private System.Threading.Thread? _thread;
        private readonly byte[] _animatedPixels;
        private readonly PlayerStartupProgress _progress;

        private WindowsSplashWindow(PlayerSplashPresentation presentation)
        {
            _title = $"{presentation.ProductName} Splash Screen";
            _imageWidth = presentation.ImageWidth;
            _imageHeight = presentation.ImageHeight;
            _pixels = presentation.BgraPixels;
            _animatedPixels = new byte[_pixels.Length];
            _progress = PlayerStartupEnvironment.Current ?? PlayerStartupEnvironment.Begin();
            _background = presentation.BackgroundColor;
        }

        public bool IsVisible => _windowHandle != 0 && IsWindowVisible(_windowHandle);

        internal static WindowsSplashWindow Start(PlayerSplashPresentation presentation)
        {
            var window = new WindowsSplashWindow(presentation);
            using var ready = new System.Threading.ManualResetEventSlim();
            Exception? failure = null;
            window._thread = new System.Threading.Thread(() =>
            {
                try
                {
                    window.Initialize();
                    ready.Set();
                    while (!window._disposed && window._windowHandle != 0)
                    {
                        _ = InvalidateRect(window._windowHandle, 0, false);
                        window.Pump();
                        System.Threading.Thread.Sleep(16);
                    }
                }
                catch (Exception exception) { failure = exception; ready.Set(); }
                finally { window.DisposeNative(); }
            }) { IsBackground = true, Name = "BEngine Startup Presentation" };
            window._thread.Start();
            ready.Wait();
            if (failure is not null) throw new InvalidOperationException("Could not show the Player logo.", failure);
            return window;
        }

        private void Initialize()
        {
            var module = GetModuleHandle(null);
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Style = CsHredraw | CsVredraw,
                WindowProcedure = SharedWindowProcedure,
                Instance = module,
                Cursor = LoadCursor(0, (nint)32512),
                ClassName = _className
            };
            _windowClass = RegisterClassEx(ref windowClass);
            if (_windowClass == 0) throw LastWin32Error("register the splash window class");

            var screenWidth = Math.Max(640, GetSystemMetrics(SmCxscreen));
            var screenHeight = Math.Max(360, GetSystemMetrics(SmCyscreen));
            var width = Math.Clamp((int)(screenWidth * 0.5), 520, 800);
            var height = Math.Clamp(width * 9 / 16, 292, Math.Max(292, screenHeight - 80));
            var x = Math.Max(0, (screenWidth - width) / 2);
            var y = Math.Max(0, (screenHeight - height) / 2);
            _ownerHandle = GCHandle.Alloc(this);
            _windowHandle = CreateWindowEx(
                WsExTopmost | WsExToolWindow,
                _className,
                _title,
                WsPopup,
                x, y, width, height,
                0, 0, module, GCHandle.ToIntPtr(_ownerHandle));
            if (_windowHandle == 0) throw LastWin32Error("create the splash window");
            _progress.RestartPresentation();
            _ = ShowWindow(_windowHandle, SwShow);
            _ = UpdateWindow(_windowHandle);
            _ = SetForegroundWindow(_windowHandle);
        }

        public void Pump()
        {
            if (_disposed || System.Threading.Thread.CurrentThread != _thread) return;
            var handle = _windowHandle;
            if (handle == 0) return;
            while (PeekMessage(out var message, handle, 0, 0, PmRemove))
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }

        private void Paint(nint windowHandle)
        {
            var displayedProgress = _progress.Progress;
            var device = BeginPaint(windowHandle, out var paint);
            if (device == 0) return;
            try
            {
                _ = GetClientRect(windowHandle, out var client);
                var brush = CreateSolidBrush(_background);
                if (brush != 0)
                {
                    _ = FillRect(device, ref client, brush);
                    _ = DeleteObject(brush);
                }

                var clientWidth = Math.Max(1, client.Right - client.Left);
                var clientHeight = Math.Max(1, client.Bottom - client.Top);
                var scale = Math.Min(Math.Min(240, clientWidth * 0.48) / _imageWidth, Math.Min(180, clientHeight * 0.32) / _imageHeight);
                var drawWidth = Math.Max(1, (int)Math.Round(_imageWidth * scale));
                var drawHeight = Math.Max(1, (int)Math.Round(_imageHeight * scale));
                var drawX = (clientWidth - drawWidth) / 2;
                var drawY = (clientHeight - drawHeight) / 2;
                var bitmapInfo = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                        Width = _imageWidth,
                        Height = -_imageHeight,
                        Planes = 1,
                        BitCount = 32,
                        Compression = BiRgb,
                        SizeImage = (uint)_pixels.Length
                    }
                };
                var opacity = _progress.LogoOpacity;
                for (var i = 0; i < _pixels.Length; i += 4)
                {
                    _animatedPixels[i] = (byte)(((_background >> 16) & 255) * (1 - opacity) + _pixels[i] * opacity);
                    _animatedPixels[i + 1] = (byte)(((_background >> 8) & 255) * (1 - opacity) + _pixels[i + 1] * opacity);
                    _animatedPixels[i + 2] = (byte)((_background & 255) * (1 - opacity) + _pixels[i + 2] * opacity);
                    _animatedPixels[i + 3] = 255;
                }
                var pinned = GCHandle.Alloc(_animatedPixels, GCHandleType.Pinned);
                try
                {
                    _ = StretchDibits(device, drawX, drawY, drawWidth, drawHeight,
                        0, 0, _imageWidth, _imageHeight, pinned.AddrOfPinnedObject(),
                        ref bitmapInfo, DibRgbColors, Srccopy);
                }
                finally
                {
                    pinned.Free();
                }
                if (_progress.ShowProgressBar)
                {
                    var barWidth = Math.Min(240, clientWidth / 2);
                    var bar = new Rectangle { Left = (clientWidth - barWidth) / 2,
                        Top = drawY + drawHeight + 28, Right = (clientWidth + barWidth) / 2,
                        Bottom = drawY + drawHeight + 33 };
                    var trackBrush = CreateSolidBrush(0x00494139);
                    var fillBrush = CreateSolidBrush(0x00dfd3c7);
                    try
                    {
                        _ = FillRect(device, ref bar, trackBrush);
                        bar.Right = bar.Left + (int)(barWidth * displayedProgress);
                        _ = FillRect(device, ref bar, fillBrush);
                    }
                    finally { _ = DeleteObject(trackBrush); _ = DeleteObject(fillBrush); }
                }
            }
            finally
            {
                _ = EndPaint(windowHandle, ref paint);
                _progress.ConfirmProgressPresented(displayedProgress);
            }
        }

        private static nint DispatchWindowMessage(nint windowHandle, uint message, nint wParam, nint lParam)
        {
            try
            {
                if (message == WmNccreate)
                {
                    var create = Marshal.PtrToStructure<CreateStructure>(lParam);
                    _ = SetWindowLongPtr(windowHandle, GwlpUserData, create.CreateParameters);
                }
                var ownerPointer = GetWindowLongPtr(windowHandle, GwlpUserData);
                var owner = ownerPointer == 0
                    ? null
                    : GCHandle.FromIntPtr(ownerPointer).Target as WindowsSplashWindow;
                switch (message)
                {
                    case WmPaint:
                        owner?.Paint(windowHandle);
                        return 0;
                    case WmClose:
                        _ = DestroyWindow(windowHandle);
                        return 0;
                    case WmDestroy:
                        owner?._windowHandle = 0;
                        return 0;
                    case WmNcdestroy:
                        _ = SetWindowLongPtr(windowHandle, GwlpUserData, 0);
                        break;
                }
            }
            catch
            {
                // Never let a managed exception escape a native window procedure.
            }
            return DefWindowProc(windowHandle, message, wParam, lParam);
        }

        public void Dispose()
        {
            _disposed = true;
            if (_thread is not null && System.Threading.Thread.CurrentThread != _thread) _thread.Join();
        }

        private void DisposeNative()
        {
            var handle = _windowHandle;
            if (handle != 0) _ = DestroyWindow(handle);
            _windowHandle = 0;
            if (_windowClass != 0)
            {
                _ = UnregisterClass(_className, GetModuleHandle(null));
                _windowClass = 0;
            }
            if (_ownerHandle.IsAllocated) _ownerHandle.Free();
        }

        private static InvalidOperationException LastWin32Error(string operation) =>
            new($"Could not {operation} (Win32 error {Marshal.GetLastWin32Error()}).");

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(nint windowHandle, nint rectangle, bool erase);

        private delegate nint WindowProc(nint windowHandle, uint message, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            internal uint Size;
            internal uint Style;
            [MarshalAs(UnmanagedType.FunctionPtr)] internal WindowProc WindowProcedure;
            internal int ClassExtraBytes;
            internal int WindowExtraBytes;
            internal nint Instance;
            internal nint Icon;
            internal nint Cursor;
            internal nint Background;
            internal string? MenuName;
            internal string ClassName;
            internal nint SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CreateStructure
        {
            internal nint CreateParameters;
            internal nint Instance;
            internal nint Menu;
            internal nint Parent;
            internal int Height;
            internal int Width;
            internal int Y;
            internal int X;
            internal int Style;
            internal nint Name;
            internal nint Class;
            internal uint ExtendedStyle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            internal nint Window;
            internal uint Id;
            internal nuint WParam;
            internal nint LParam;
            internal uint Time;
            internal Point Point;
            internal uint Private;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rectangle
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintStructure
        {
            internal nint Device;
            internal int Erase;
            internal Rectangle Paint;
            internal int Restore;
            internal int Update;
            internal int Reserved0;
            internal int Reserved1;
            internal int Reserved2;
            internal int Reserved3;
            internal int Reserved4;
            internal int Reserved5;
            internal int Reserved6;
            internal int Reserved7;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width;
            internal int Height;
            internal ushort Planes;
            internal ushort BitCount;
            internal uint Compression;
            internal uint SizeImage;
            internal int XPixelsPerMeter;
            internal int YPixelsPerMeter;
            internal uint ColorsUsed;
            internal uint ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal BitmapInfoHeader Header;
            internal uint Colors;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WindowClass windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterClass(string className, nint instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(
            uint extendedStyle, string className, string windowName, uint style,
            int x, int y, int width, int height,
            nint parent, nint menu, nint instance, nint parameters);

        [DllImport("user32.dll")]
        private static extern nint DefWindowProc(nint windowHandle, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint value);

        [DllImport("user32.dll")]
        private static extern nint GetWindowLongPtr(nint windowHandle, int index);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(nint windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(
            out Message message,
            nint windowHandle,
            uint minimum,
            uint maximum,
            uint removeMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern nint LoadCursor(nint instance, nint cursorName);

        [DllImport("user32.dll")]
        private static extern nint BeginPaint(nint windowHandle, out PaintStructure paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EndPaint(nint windowHandle, ref PaintStructure paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(nint windowHandle, out Rectangle rectangle);

        [DllImport("gdi32.dll")]
        private static extern nint CreateSolidBrush(uint color);

        [DllImport("user32.dll")]
        private static extern int FillRect(nint device, ref Rectangle rectangle, nint brush);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll")]
        private static extern int StretchDibits(
            nint device, int destinationX, int destinationY, int destinationWidth, int destinationHeight,
            int sourceX, int sourceY, int sourceWidth, int sourceHeight, nint bits,
            ref BitmapInfo bitmapInfo, uint usage, uint operation);
    }
}
