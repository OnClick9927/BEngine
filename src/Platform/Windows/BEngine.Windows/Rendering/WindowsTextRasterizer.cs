using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Platform;

public sealed class WindowsTextRasterizer : ITextRasterizer
{
    private const uint DtWordBreak = 0x0010;
    private const uint DtSingleLine = 0x0020;
    private const uint DtCalculateRect = 0x0400;
    private const uint DtNoPrefix = 0x0800;
    private const uint DtEndEllipsis = 0x8000;
    private const string DefaultFontFamily = "Microsoft YaHei UI";
    private readonly string _fontFamily;
    private readonly Lock _measurementGate = new();
    private readonly Dictionary<(string Text, float Size), (float Width, float Height)> _measurements = [];
    private readonly Queue<(string Text, float Size)> _measurementOrder = [];
    private const int MeasurementCapacity = 2048;
    public WindowsTextRasterizer() : this(DefaultFontFamily) { }
    public WindowsTextRasterizer(string fontFamily) => _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily;
    public ITextRasterizer ForFontFamily(string family)
    {
        var selected = string.IsNullOrWhiteSpace(family) ? DefaultFontFamily : family.Trim();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var dc = CreateCompatibleDC(IntPtr.Zero); var font = IntPtr.Zero; var previous = IntPtr.Zero;
        try
        {
            if (dc == IntPtr.Zero) throw new InvalidOperationException("Cannot create a font measurement context.");
            font = CreateTextFont(64, selected); previous = SelectObject(dc, font);
            var face = new System.Text.StringBuilder(128);
            if (font == IntPtr.Zero || GetTextFaceW(dc, face.Capacity, face) == 0 ||
                !face.ToString().Equals(selected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The font family '{selected}' is not installed. Windows selected '{face}'.");
            return new WindowsTextRasterizer(selected);
        }
        finally
        {
            if (previous != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, previous);
            if (font != IntPtr.Zero) DeleteObject(font);
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }

    public float GetHorizontalPadding(float fontSize) => 2 * TextInset(fontSize) + 2;

    private static int TextInset(float fontSize) => Math.Min(6, Math.Max(0, (int)Math.Round(fontSize / 3)));

    public bool TryMeasureText(string text, float fontSize, out float width, out float height)
    {
        var key = (text ?? "", fontSize);
        lock (_measurementGate)
            if (_measurements.TryGetValue(key, out var cached))
            { width = cached.Width; height = cached.Height; return true; }
        var result = TryMeasureSize(string.IsNullOrEmpty(text) ? " " : text, fontSize, _fontFamily, out var measuredWidth, out var measuredHeight);
        width = string.IsNullOrEmpty(text) ? 0 : measuredWidth;
        height = measuredHeight;
        if (result)
        {
            lock (_measurementGate)
                if (!_measurements.ContainsKey(key))
                {
                    // Retain dimensions, never GDI handles. Animated text and
                    // localization cannot grow this per-font cache indefinitely.
                    if (_measurements.Count >= MeasurementCapacity)
                        _measurements.Remove(_measurementOrder.Dequeue());
                    _measurements.Add(key, (width, height));
                    _measurementOrder.Enqueue(key);
                }
        }
        return result;
    }

    public bool TryResolveText(string text, int width, int height, float fontSize,
        out RasterizedImage texture) =>
        TryResolveText(text, width, height, fontSize, wordWrap: false, out texture);

    public bool TryResolveText(string text, int width, int height, float fontSize, bool wordWrap,
        out RasterizedImage texture) =>
        TryRasterize(text, width, height, fontSize, _fontFamily, out texture, wordWrap,
            inset: TextInset(fontSize));

    public static bool TryMeasure(string text, float fontSize, string fontFamily, out int width, out int height)
    {
        var measured = TryMeasureSize(text, fontSize, fontFamily, out width, out height);
        if (measured) width = Math.Max(1, width + 8);
        return measured;
    }

    public static bool TryMeasureAdvance(string text, float fontSize, string fontFamily, out int width) =>
        TryMeasureSize(text, fontSize, fontFamily, out width, out _);

    private static bool TryMeasureSize(string text, float fontSize, string fontFamily, out int width, out int height)
    {
        width = height = 0;
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(text) || !float.IsFinite(fontSize)) return false;
        IntPtr dc = IntPtr.Zero, font = IntPtr.Zero, oldFont = IntPtr.Zero;
        try
        {
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            font = CreateTextFont(fontSize, fontFamily);
            if (font == IntPtr.Zero) return false;
            oldFont = SelectObject(dc, font);
            if (!GetTextExtentPoint32W(dc, text, text.Length, out var size)) return false;
            width = Math.Max(0, size.Width);
            height = Math.Max(1, size.Height);
            return true;
        }
        catch (Exception exception) when (exception is ExternalException or OverflowException)
        {
            return false;
        }
        finally
        {
            if (oldFont != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, oldFont);
            if (font != IntPtr.Zero) DeleteObject(font);
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }

    public static unsafe bool TryRasterize(string text, int width, int height, float fontSize, string fontFamily,
        out RasterizedImage texture, bool wordWrap = false, int inset = 0)
    {
        texture = default;
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(text) || width <= 0 || height <= 0 ||
            !float.IsFinite(fontSize)) return false;
        IntPtr dc = IntPtr.Zero, bitmap = IntPtr.Zero, font = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero, oldFont = IntPtr.Zero;
        try
        {
            var byteCount = checked(width * height * 4);
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height,
                    Planes = 1, BitCount = 32, Compression = 0
                }
            };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return false;
            oldBitmap = SelectObject(dc, bitmap);
            // Clear the actual drawing surface. No managed output is needed
            // until GDI has successfully measured and drawn the text.
            new Span<byte>((void*)bits, byteCount).Clear();
            SetBkMode(dc, 1);
            SetTextColor(dc, 0x00FFFFFF);
            inset = Math.Clamp(inset, 0, Math.Max(0, (width - 1) / 2));
            var flags = DtNoPrefix | (wordWrap ? DtWordBreak : DtSingleLine | DtEndEllipsis);
            var fittedSize = Math.Max(1, fontSize);
            NativeRect rect;
            while (true)
            {
                var nextFont = CreateTextFont(fittedSize, fontFamily);
                if (nextFont == IntPtr.Zero) return false;
                var previousFont = SelectObject(dc, nextFont);
                font = nextFont;
                if (oldFont == IntPtr.Zero) oldFont = previousFont;
                else DeleteObject(previousFont);
                rect = new NativeRect { Left = inset, Right = Math.Max(inset + 1, width - inset - 2) };
                if (DrawTextW(dc, text, text.Length, ref rect, flags | DtCalculateRect) == 0) return false;
                if (!wordWrap || rect.Bottom <= height || fittedSize <= 1) break;
                fittedSize = Math.Max(1, fittedSize - 1);
            }
            var textHeight = rect.Bottom;
            rect = new NativeRect
            {
                Left = inset, Top = Math.Max(0, (height - textHeight) / 2),
                Right = Math.Max(inset + 1, width - inset - 2), Bottom = height
            };
            if (DrawTextW(dc, text, text.Length, ref rect, flags) == 0) return false;
            // GDI batches drawing; complete it before reading the DIB's coverage mask.
            GdiFlush();
            var pixels = GC.AllocateUninitializedArray<byte>(byteCount);
            var coverage = new ReadOnlySpan<uint>((void*)bits, byteCount / sizeof(uint));
            var output = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
            uint ink = 0;
            for (var index = 0; index < coverage.Length; index++)
            {
                // Windows DIBs are little-endian BGRA. Ignore their undefined
                // alpha just as before, and write every owned RGBA byte once.
                var pixel = coverage[index];
                var alpha = Math.Max(pixel & 255u, Math.Max((pixel >> 8) & 255u, (pixel >> 16) & 255u));
                ink |= alpha;
                output[index] = 0x00FFFFFFu | (alpha << 24);
            }
            texture = new RasterizedImage(width, height, GraphicsTextureFormat.Rgba8Unorm, pixels);
            return ink != 0;
        }
        catch (Exception exception) when (exception is ExternalException or OverflowException)
        {
            return false;
        }
        finally
        {
            if (oldFont != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, oldFont);
            if (oldBitmap != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, oldBitmap);
            if (font != IntPtr.Zero) DeleteObject(font);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }

    private static IntPtr CreateTextFont(float fontSize, string fontFamily)
    {
        var face = string.IsNullOrWhiteSpace(fontFamily) ||
                   fontFamily.Equals("BEngine Built-in", StringComparison.OrdinalIgnoreCase)
            ? DefaultFontFamily : fontFamily;
        return CreateFontW(-Math.Max(1, (int)Math.Round(fontSize)), 0, 0, 0, 400,
            0, 0, 0, 1, 0, 0, 4, 0, face);
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetTextFaceW(IntPtr dc, int count, System.Text.StringBuilder name);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint RedMask, GreenMask, BlueMask; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }

    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr CreateDIBSection(IntPtr hdc,
        ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int DrawTextW(IntPtr hdc, string text, int length, ref NativeRect rect, uint format);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32W(IntPtr hdc, string text, int length, out NativeSize size);
}
