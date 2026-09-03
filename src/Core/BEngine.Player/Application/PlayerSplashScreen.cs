using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BEngine.Build;
using BEngine.Content;
using BEngine.Rendering;

namespace BEngine.Player;

/// <summary>Owns the platform splash window shown before the project's AOT startup flow.</summary>
public sealed class PlayerSplashScreen : IDisposable
{
    private readonly WindowsSplashWindow? _window;
    private readonly TimeSpan _minimumDuration;
    private readonly long _shownTimestamp;
    private int _closed;

    private PlayerSplashScreen(WindowsSplashWindow? window, TimeSpan minimumDuration)
    {
        _window = window;
        _minimumDuration = minimumDuration;
        _shownTimestamp = Stopwatch.GetTimestamp();
    }

    public bool IsVisible => OperatingSystem.IsWindows() &&
                             Volatile.Read(ref _closed) == 0 && _window?.IsVisible == true;

    public static PlayerSplashScreen Show(PlayerBootstrapManifest? manifest, string playerDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        if (manifest is null || !manifest.SplashScreenEnabled)
            return new PlayerSplashScreen(null, TimeSpan.Zero);
        manifest.Validate();
        var minimumDuration = TimeSpan.FromSeconds(manifest.SplashMinimumDurationSeconds);
        if (!OperatingSystem.IsWindows())
            return new PlayerSplashScreen(null, minimumDuration);
        if (manifest.Version >= 4)
        {
            var archive = BuiltInResourceArchive.Open(
                manifest.ResolvePlayerResourceArchive(playerDirectory));
            return Show(
                archive.ReadBytes(manifest.SplashImageResource),
                manifest.ProductName,
                manifest.SplashBackgroundColor,
                minimumDuration);
        }
        var imagePath = manifest.ResolveSplashImage(playerDirectory) ??
                        throw new InvalidDataException("The enabled Player splash screen has no image path.");
        return Show(imagePath, manifest.ProductName, manifest.SplashBackgroundColor,
            minimumDuration);
    }

    public static PlayerSplashScreen Show(
        string imagePath,
        string productName,
        string backgroundColor = "#10161A",
        TimeSpan minimumDuration = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        if (minimumDuration < TimeSpan.Zero || minimumDuration > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        if (!OperatingSystem.IsWindows())
            return new PlayerSplashScreen(null, minimumDuration);

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The Player splash image was not found.", fullPath);
        if (new FileInfo(fullPath).Length > 64 * 1024 * 1024)
            throw new InvalidDataException("The Player splash image exceeds 64 MiB.");
        return Show(File.ReadAllBytes(fullPath), productName, backgroundColor, minimumDuration);
    }

    public static PlayerSplashScreen Show(
        ReadOnlySpan<byte> imageBytes,
        string productName,
        string backgroundColor = "#10161A",
        TimeSpan minimumDuration = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        if (minimumDuration < TimeSpan.Zero || minimumDuration > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        if (!OperatingSystem.IsWindows())
            return new PlayerSplashScreen(null, minimumDuration);
        if (imageBytes.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("The Player splash image exceeds 64 MiB.");
        if (!PngImageCodec.TryDecode(imageBytes,
                out var width, out var height, out var rgba) || width > 8192 || height > 8192)
            throw new InvalidDataException("The Player splash image is not a supported PNG or exceeds 8192x8192.");

        var color = ParseColor(backgroundColor);
        var window = WindowsSplashWindow.Start(productName, width, height,
            CompositeToBgra(rgba, color), color);
        return new PlayerSplashScreen(window, minimumDuration);
    }

    public void WaitForMinimumDuration(CancellationToken cancellationToken = default)
    {
        if (_minimumDuration <= TimeSpan.Zero) return;
        var remaining = _minimumDuration - Stopwatch.GetElapsedTime(_shownTimestamp);
        if (remaining <= TimeSpan.Zero) return;
        if (cancellationToken.WaitHandle.WaitOne(remaining))
            cancellationToken.ThrowIfCancellationRequested();
    }

    public async BValueTask WaitForMinimumDurationAsync(CancellationToken cancellationToken = default)
    {
        if (_minimumDuration <= TimeSpan.Zero) return;
        var remaining = _minimumDuration - Stopwatch.GetElapsedTime(_shownTimestamp);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        if (OperatingSystem.IsWindows()) _window?.Dispose();
    }

    public void CloseAfterMinimumDuration(CancellationToken cancellationToken = default)
    {
        WaitForMinimumDuration(cancellationToken);
        Close();
    }

    public async BValueTask CloseAfterMinimumDurationAsync(CancellationToken cancellationToken = default)
    {
        await WaitForMinimumDurationAsync(cancellationToken).ConfigureAwait(false);
        Close();
    }

    public void Dispose() => Close();

    private static SplashColor ParseColor(string value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var packed))
            throw new InvalidDataException("Splash background color must use #RRGGBB or #RRGGBBAA format.");
        if (value.Length == 7) packed = (packed << 8) | 0xff;
        return new SplashColor(
            (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private static byte[] CompositeToBgra(byte[] rgba, SplashColor background)
    {
        var bgra = new byte[rgba.Length];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            var alpha = rgba[index + 3];
            var inverse = 255 - alpha;
            bgra[index] = Blend(rgba[index + 2], background.Blue, alpha, inverse);
            bgra[index + 1] = Blend(rgba[index + 1], background.Green, alpha, inverse);
            bgra[index + 2] = Blend(rgba[index], background.Red, alpha, inverse);
            bgra[index + 3] = 255;
        }
        return bgra;
    }

    private static byte Blend(byte foreground, byte background, int alpha, int inverse) =>
        (byte)((foreground * alpha + background * inverse + 127) / 255);

    private readonly record struct SplashColor(byte Red, byte Green, byte Blue, byte Alpha)
    {
        internal uint ColorRef => (uint)(Red | Green << 8 | Blue << 16);
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsSplashWindow : IDisposable
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
        private static readonly WindowProc SharedWindowProcedure = DispatchWindowMessage;

        private readonly string _className = $"BEngine.Player.Splash.{Guid.NewGuid():N}";
        private readonly string _title;
        private readonly int _imageWidth;
        private readonly int _imageHeight;
        private readonly byte[] _pixels;
        private readonly SplashColor _background;
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly Thread _thread;
        private Exception? _startupFailure;
        private nint _windowHandle;
        private ushort _windowClass;
        private int _disposed;

        private WindowsSplashWindow(
            string productName,
            int imageWidth,
            int imageHeight,
            byte[] pixels,
            SplashColor background)
        {
            _title = $"{productName} Splash Screen";
            _imageWidth = imageWidth;
            _imageHeight = imageHeight;
            _pixels = pixels;
            _background = background;
            _thread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "BEngine Player Splash"
            };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        internal bool IsVisible => _windowHandle != 0 && IsWindowVisible(_windowHandle);

        internal static WindowsSplashWindow Start(
            string productName,
            int imageWidth,
            int imageHeight,
            byte[] pixels,
            SplashColor background)
        {
            var window = new WindowsSplashWindow(productName, imageWidth, imageHeight, pixels, background);
            window._thread.Start();
            if (!window._ready.Wait(TimeSpan.FromSeconds(5)))
            {
                window.Dispose();
                throw new TimeoutException("The Windows Player splash screen did not initialize in time.");
            }
            if (window._startupFailure is { } failure)
            {
                window.Dispose();
                throw new InvalidOperationException("The Windows Player splash screen could not start.", failure);
            }
            return window;
        }

        private void MessageLoop()
        {
            GCHandle ownerHandle = default;
            try
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
                ownerHandle = GCHandle.Alloc(this);
                _windowHandle = CreateWindowEx(
                    WsExTopmost | WsExToolWindow,
                    _className,
                    _title,
                    WsPopup,
                    x, y, width, height,
                    0, 0, module, GCHandle.ToIntPtr(ownerHandle));
                if (_windowHandle == 0) throw LastWin32Error("create the splash window");
                _ = ShowWindow(_windowHandle, SwShow);
                _ = UpdateWindow(_windowHandle);
                _ = SetForegroundWindow(_windowHandle);
                _ready.Set();

                while (GetMessage(out var message, 0, 0, 0) > 0)
                {
                    _ = TranslateMessage(ref message);
                    _ = DispatchMessage(ref message);
                }
            }
            catch (Exception exception)
            {
                _startupFailure = exception;
                _ready.Set();
            }
            finally
            {
                _windowHandle = 0;
                if (_windowClass != 0)
                {
                    _ = UnregisterClass(_className, GetModuleHandle(null));
                    _windowClass = 0;
                }
                if (ownerHandle.IsAllocated) ownerHandle.Free();
            }
        }

        private void Paint(nint windowHandle)
        {
            var device = BeginPaint(windowHandle, out var paint);
            if (device == 0) return;
            try
            {
                _ = GetClientRect(windowHandle, out var client);
                var brush = CreateSolidBrush(_background.ColorRef);
                if (brush != 0)
                {
                    _ = FillRect(device, ref client, brush);
                    _ = DeleteObject(brush);
                }

                var clientWidth = Math.Max(1, client.Right - client.Left);
                var clientHeight = Math.Max(1, client.Bottom - client.Top);
                var scale = Math.Min(clientWidth * 0.64 / _imageWidth, clientHeight * 0.72 / _imageHeight);
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
                var pinned = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
                try
                {
                    _ = StretchDibits(device, drawX, drawY, drawWidth, drawHeight,
                        0, 0, _imageWidth, _imageHeight, pinned.AddrOfPinnedObject(),
                        ref bitmapInfo, DibRgbColors, Srccopy);
                }
                finally { pinned.Free(); }
            }
            finally { _ = EndPaint(windowHandle, ref paint); }
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
                        PostQuitMessage(0);
                        return 0;
                    case WmNcdestroy:
                        _ = SetWindowLongPtr(windowHandle, GwlpUserData, 0);
                        break;
                }
            }
            catch { }
            return DefWindowProc(windowHandle, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var handle = _windowHandle;
            if (handle != 0) _ = PostMessage(handle, WmClose, 0, 0);
            if (_thread.IsAlive && !ReferenceEquals(Thread.CurrentThread, _thread))
                _thread.Join(TimeSpan.FromSeconds(2));
            _ready.Dispose();
        }

        private static InvalidOperationException LastWin32Error(string operation) =>
            new($"Could not {operation} (Win32 error {Marshal.GetLastWin32Error()}).");

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
        private struct Point { internal int X; internal int Y; }

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
        private static extern bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out Message message, nint windowHandle, uint minimum, uint maximum);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int exitCode);

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
