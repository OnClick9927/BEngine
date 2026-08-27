using System.Runtime.InteropServices;
using BEngine.Editor.Rendering;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Editor image and Unicode text resources. Text is rasterized once, then composited by the GPU canvas.</summary>
internal sealed class EditorGpuCanvasResourceResolver : IGpuCanvasResourceResolver, IGpuCanvasTextResolver
{
    public static EditorGpuCanvasResourceResolver Shared { get; } = new();

    public bool TryResolveTexture(string source, out GpuCanvasTextureData texture) =>
        FileGpuCanvasResourceResolver.Shared.TryResolveTexture(source, out texture);

    public bool TryMeasureText(string text, float fontSize, string fontFamily, out int width)
    {
        width = 0;
        return OperatingSystem.IsWindows() &&
               WindowsTextRasterizer.TryMeasure(text, fontSize, fontFamily, out width, out _);
    }

    internal bool TryMeasureTextAdvance(string text, float fontSize, string fontFamily, out int width)
    {
        width = 0;
        return OperatingSystem.IsWindows() &&
               WindowsTextRasterizer.TryMeasureAdvance(text, fontSize, fontFamily, out width);
    }

    internal bool TryMeasureLineHeight(float fontSize, string fontFamily, out int height)
    {
        height = 0;
        return OperatingSystem.IsWindows() &&
               WindowsTextRasterizer.TryMeasure("Ag", fontSize, fontFamily, out _, out height);
    }

    public bool TryResolveText(string text, int width, int height, float fontSize, string fontFamily,
        out GpuCanvasTextureData texture)
    {
        texture = default;
        return OperatingSystem.IsWindows() &&
               WindowsTextRasterizer.TryRasterize(text, width, height, fontSize, fontFamily, out texture);
    }

    private static class WindowsTextRasterizer
    {
        private const int Transparent = 1;
        private const uint DibRgbColors = 0;
        private const uint DtLeft = 0x0000;
        private const uint DtVCenter = 0x0004;
        private const uint DtSingleLine = 0x0020;
        private const uint DtEndEllipsis = 0x8000;
        private const uint DtNoPrefix = 0x0800;

        public static bool TryMeasure(string text, float fontSize, string fontFamily, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(text)) return false;
            IntPtr dc = IntPtr.Zero, font = IntPtr.Zero, oldFont = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                font = CreateTextFont(fontSize, fontFamily);
                if (font != IntPtr.Zero) oldFont = SelectObject(dc, font);
                if (!GetTextExtentPoint32W(dc, text, text.Length, out var size)) return false;
                width = Math.Max(1, size.Width + 8);
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

        public static bool TryMeasureAdvance(string text, float fontSize, string fontFamily, out int width)
        {
            width = 0;
            if (string.IsNullOrEmpty(text)) return false;
            IntPtr dc = IntPtr.Zero, font = IntPtr.Zero, oldFont = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                font = CreateTextFont(fontSize, fontFamily);
                if (font != IntPtr.Zero) oldFont = SelectObject(dc, font);
                if (!GetTextExtentPoint32W(dc, text, text.Length, out var size)) return false;
                width = Math.Max(0, size.Width);
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

        public static bool TryRasterize(string text, int width, int height, float fontSize, string fontFamily,
            out GpuCanvasTextureData texture)
        {
            texture = default;
            if (string.IsNullOrEmpty(text) || width <= 0 || height <= 0) return false;
            IntPtr dc = IntPtr.Zero, bitmap = IntPtr.Zero, font = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero, oldFont = IntPtr.Zero;
            try
            {
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
                bitmap = CreateDIBSection(dc, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return false;
                oldBitmap = SelectObject(dc, bitmap);
                font = CreateTextFont(fontSize, fontFamily);
                if (font != IntPtr.Zero) oldFont = SelectObject(dc, font);
                SetBkMode(dc, Transparent);
                SetTextColor(dc, 0x00FFFFFF);
                var rect = new NativeRect { Left = 0, Top = 0, Right = Math.Max(1, width - 2), Bottom = height };
                DrawTextW(dc, text, text.Length, ref rect,
                    DtLeft | DtVCenter | DtSingleLine | DtEndEllipsis | DtNoPrefix);
                var bgra = new byte[checked(width * height * 4)];
                Marshal.Copy(bits, bgra, 0, bgra.Length);
                var rgba = new byte[bgra.Length];
                for (var offset = 0; offset < bgra.Length; offset += 4)
                {
                    var alpha = Math.Max(bgra[offset], Math.Max(bgra[offset + 1], bgra[offset + 2]));
                    rgba[offset] = 255; rgba[offset + 1] = 255; rgba[offset + 2] = 255; rgba[offset + 3] = alpha;
                }
                texture = new GpuCanvasTextureData(width, height, GraphicsTextureFormat.Rgba8Unorm, rgba);
                for (var offset = 3; offset < rgba.Length; offset += 4)
                    if (rgba[offset] != 0) return true;
                return false;
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
                ? "Microsoft YaHei UI"
                : fontFamily;
            return CreateFontW(-Math.Max(1, (int)Math.Round(fontSize)), 0, 0, 0, 400,
                0, 0, 0, 1, 0, 0, 5, 0, face);
        }

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
}
