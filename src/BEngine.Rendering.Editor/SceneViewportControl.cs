using System.ComponentModel;
using System.Runtime.InteropServices;
using BEngine;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using BEngine.UIElements.Editor;
using Silk.NET.OpenGL;

namespace BEngine.Rendering.Editor;

internal sealed class SceneViewportControl : UserControl
{
    private readonly Func<Scene> _scene;
    private readonly Func<RenderCamera?> _camera;
    private readonly Func<bool> _drawGrid;
    private readonly bool _drawUi;
    private readonly bool _drawGizmos;
    private readonly Action<float, float>? _orbit;
    private readonly Action<float>? _zoom;
    private readonly Label _status;
    private readonly string _emptyCameraMessage;
    private IntPtr _deviceContext;
    private IntPtr _renderContext;
    private IntPtr _openGlLibrary;
    private GL? _gl;
    private EngineRenderer? _renderer;
    private bool _orbiting;
    private Point _lastMousePosition;
    private bool _loaded;
    private bool _disposed;
    private string? _lastError;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsOwnDc;
            return parameters;
        }
    }

    public SceneViewportControl(
        Func<Scene> scene,
        Func<RenderCamera?> camera,
        Func<bool> drawGrid,
        bool drawUi,
        Action<float, float>? orbit = null,
        Action<float>? zoom = null,
        bool drawGizmos = false,
        string emptyCameraMessage = "No active camera")
    {
        _scene = scene;
        _camera = camera;
        _drawGrid = drawGrid;
        _drawUi = drawUi;
        _drawGizmos = drawGizmos;
        _orbit = orbit;
        _zoom = zoom;
        _emptyCameraMessage = emptyCameraMessage;

        Dock = DockStyle.Fill;
        BackColor = UIElementsTheme.Field;
        SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        _status = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = UIElementsTheme.Field,
            ForeColor = UIElementsTheme.TextDisabled,
            Font = UIElementsTheme.Font(10f),
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false
        };
        Controls.Add(_status);

        MouseEnter += (_, _) => Focus();
        MouseDown += OnViewportMouseDown;
        MouseUp += OnViewportMouseUp;
        MouseMove += OnViewportMouseMove;
        MouseWheel += (_, args) => _zoom?.Invoke(args.Delta / 120f);
        LostFocus += (_, _) => StopOrbiting();
    }

    public void Tick()
    {
        if (!_loaded || _disposed || !IsHandleCreated || !Visible ||
            _deviceContext == IntPtr.Zero || _renderContext == IntPtr.Zero || _renderer is null)
        {
            return;
        }

        var camera = _camera();
        if (camera is null)
        {
            ShowStatus(_emptyCameraMessage);
            return;
        }

        try
        {
            if (!WglMakeCurrent(_deviceContext, _renderContext))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not activate the OpenGL viewport context.");
            }

            var width = Math.Max(1, ClientSize.Width);
            var height = Math.Max(1, ClientSize.Height);
            _renderer.Render(_scene(), camera.Value, width, height,
                drawGrid: _drawGrid(), drawUi: _drawUi, drawGizmos: _drawGizmos);
            if (!SwapBuffers(_deviceContext))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not present the OpenGL viewport.");
            }

            ShowStatus(null);
        }
        catch (Exception exception)
        {
            ReportError("Viewport rendering failed", exception);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode && !_disposed) CreateContext();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        DestroyContext();
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!_loaded) base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!_loaded) base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            DestroyContext();
        }
        base.Dispose(disposing);
    }

    private void CreateContext()
    {
        DestroyContext();
        try
        {
            _deviceContext = GetDC(Handle);
            if (_deviceContext == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not acquire the viewport device context.");
            }

            var descriptor = PixelFormatDescriptor.Create();
            var pixelFormat = GetPixelFormat(_deviceContext);
            if (pixelFormat == 0)
            {
                pixelFormat = ChoosePixelFormat(_deviceContext, ref descriptor);
                if (pixelFormat == 0 || !SetPixelFormat(_deviceContext, pixelFormat, ref descriptor))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Could not configure the viewport pixel format.");
                }
            }

            var legacyContext = WglCreateContext(_deviceContext);
            if (legacyContext == IntPtr.Zero || !WglMakeCurrent(_deviceContext, legacyContext))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the OpenGL viewport context.");
            }

            _renderContext = legacyContext;
            _renderContext = CreateCoreContext(_renderContext);
            _openGlLibrary = NativeLibrary.Load("opengl32.dll");
            _gl = GL.GetApi(ResolveOpenGlProcedure);
            var graphicsDevices = new GraphicsDeviceFactory();
            graphicsDevices.RegisterProvider(new OpenGlGraphicsDeviceProvider(_gl));
            _renderer = new EngineRenderer(
                graphicsDevices.CreateDevice(GraphicsBackend.OpenGL),
                ownsGraphicsDevice: true);
            _loaded = true;
            _lastError = null;
            ShowStatus(null);
        }
        catch (Exception exception)
        {
            ReportError("OpenGL viewport initialization failed", exception);
            DestroyContext(preserveStatus: true);
        }
    }

    private IntPtr CreateCoreContext(IntPtr legacyContext)
    {
        var address = WglGetProcAddress("wglCreateContextAttribsARB");
        if (!IsValidProcedure(address)) return legacyContext;

        var createContext = Marshal.GetDelegateForFunctionPointer<WglCreateContextAttribs>(address);
        int[] attributes =
        [
            WglContextMajorVersion, 3,
            WglContextMinorVersion, 3,
            WglContextProfileMask, WglContextCoreProfileBit,
            0
        ];
        var coreContext = createContext(_deviceContext, IntPtr.Zero, attributes);
        if (coreContext == IntPtr.Zero) return legacyContext;
        if (!WglMakeCurrent(_deviceContext, coreContext))
        {
            WglDeleteContext(coreContext);
            WglMakeCurrent(_deviceContext, legacyContext);
            return legacyContext;
        }

        WglDeleteContext(legacyContext);
        return coreContext;
    }

    private IntPtr ResolveOpenGlProcedure(string name)
    {
        var address = WglGetProcAddress(name);
        if (IsValidProcedure(address)) return address;
        return _openGlLibrary != IntPtr.Zero && NativeLibrary.TryGetExport(_openGlLibrary, name, out address)
            ? address
            : IntPtr.Zero;
    }

    private void DestroyContext(bool preserveStatus = false)
    {
        _loaded = false;
        var canReleaseGl = _deviceContext != IntPtr.Zero && _renderContext != IntPtr.Zero &&
                           WglMakeCurrent(_deviceContext, _renderContext);
        if (canReleaseGl) _renderer?.Dispose();
        _renderer = null;
        _gl?.Dispose();
        _gl = null;

        if (_renderContext != IntPtr.Zero)
        {
            WglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            WglDeleteContext(_renderContext);
            _renderContext = IntPtr.Zero;
        }
        if (_deviceContext != IntPtr.Zero && IsHandleCreated)
        {
            ReleaseDC(Handle, _deviceContext);
            _deviceContext = IntPtr.Zero;
        }
        if (_openGlLibrary != IntPtr.Zero)
        {
            NativeLibrary.Free(_openGlLibrary);
            _openGlLibrary = IntPtr.Zero;
        }

        if (!preserveStatus) ShowStatus(null);
    }

    private void OnViewportMouseDown(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Right || _orbit is null) return;
        _orbiting = true;
        _lastMousePosition = args.Location;
        Capture = true;
    }

    private void OnViewportMouseUp(object? sender, MouseEventArgs args)
    {
        if (args.Button == MouseButtons.Right) StopOrbiting();
    }

    private void OnViewportMouseMove(object? sender, MouseEventArgs args)
    {
        if (!_orbiting) return;
        var deltaX = args.X - _lastMousePosition.X;
        var deltaY = args.Y - _lastMousePosition.Y;
        _lastMousePosition = args.Location;
        _orbit?.Invoke(deltaX, deltaY);
    }

    private void StopOrbiting()
    {
        _orbiting = false;
        Capture = false;
    }

    private void ReportError(string message, Exception exception)
    {
        var detail = $"{message}: {exception.Message}";
        ShowStatus(detail);
        if (string.Equals(_lastError, detail, StringComparison.Ordinal)) return;
        _lastError = detail;
        BEngine.Debug.LogError(detail);
    }

    private void ShowStatus(string? message)
    {
        if (_status.IsDisposed) return;
        _status.Text = message ?? string.Empty;
        _status.Visible = !string.IsNullOrWhiteSpace(message);
        if (_status.Visible) _status.BringToFront();
    }

    private static bool IsValidProcedure(IntPtr address)
    {
        var value = address.ToInt64();
        return value is not 0 and not 1 and not 2 and not 3 and not -1;
    }

    private const uint PfdDoubleBuffer = 0x00000001;
    private const uint PfdDrawToWindow = 0x00000004;
    private const uint PfdSupportOpenGl = 0x00000020;
    private const byte PfdTypeRgba = 0;
    private const byte PfdMainPlane = 0;
    private const int WglContextMajorVersion = 0x2091;
    private const int WglContextMinorVersion = 0x2092;
    private const int WglContextProfileMask = 0x9126;
    private const int WglContextCoreProfileBit = 0x00000001;
    private const int CsOwnDc = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size;
        public ushort Version;
        public uint Flags;
        public byte PixelType;
        public byte ColorBits;
        public byte RedBits;
        public byte RedShift;
        public byte GreenBits;
        public byte GreenShift;
        public byte BlueBits;
        public byte BlueShift;
        public byte AlphaBits;
        public byte AlphaShift;
        public byte AccumBits;
        public byte AccumRedBits;
        public byte AccumGreenBits;
        public byte AccumBlueBits;
        public byte AccumAlphaBits;
        public byte DepthBits;
        public byte StencilBits;
        public byte AuxiliaryBuffers;
        public byte LayerType;
        public byte Reserved;
        public uint LayerMask;
        public uint VisibleMask;
        public uint DamageMask;

        public static PixelFormatDescriptor Create() => new()
        {
            Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
            Version = 1,
            Flags = PfdDrawToWindow | PfdSupportOpenGl | PfdDoubleBuffer,
            PixelType = PfdTypeRgba,
            ColorBits = 32,
            AlphaBits = 8,
            DepthBits = 24,
            StencilBits = 8,
            LayerType = PfdMainPlane
        };
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WglCreateContextAttribs(IntPtr deviceContext, IntPtr sharedContext,
        [In] int[] attributes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int ChoosePixelFormat(IntPtr deviceContext, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPixelFormat(IntPtr deviceContext, int format,
        ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll")]
    private static extern int GetPixelFormat(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SwapBuffers(IntPtr deviceContext);

    [DllImport("opengl32.dll", EntryPoint = "wglCreateContext", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr WglCreateContext(IntPtr deviceContext);

    [DllImport("opengl32.dll", EntryPoint = "wglDeleteContext", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WglDeleteContext(IntPtr renderContext);

    [DllImport("opengl32.dll", EntryPoint = "wglMakeCurrent", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WglMakeCurrent(IntPtr deviceContext, IntPtr renderContext);

    [DllImport("opengl32.dll", EntryPoint = "wglGetProcAddress", ExactSpelling = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr WglGetProcAddress(string name);
}
