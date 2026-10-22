namespace BEngine.Rendering;

/// <summary>CPU texture preprocessing shared by importers and graphics uploaders.</summary>
public static class TexturePixelProcessing
{
    public static byte[] ResizeRgba(ReadOnlySpan<byte> pixels, int width, int height, int targetWidth, int targetHeight,
        bool pointFilter = false, bool sRgb = false)
    {
        if (width <= 0 || height <= 0 || targetWidth <= 0 || targetHeight <= 0 || targetWidth > width || targetHeight > height)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Resize accepts positive dimensions and downscaling only.");
        if (pixels.Length != checked(width * height * 4)) throw new ArgumentException("Invalid RGBA data length.", nameof(pixels));
        if (width == targetWidth && height == targetHeight) return pixels.ToArray();
        var output = new byte[checked(targetWidth * targetHeight * 4)];
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(width, height) + 1);
        for (var y = 0; y < targetHeight; y++)
        for (var x = 0; x < targetWidth; x++)
        {
            var target = (y * targetWidth + x) * 4;
            if (pointFilter)
            {
                var sx = Math.Min(width - 1, (int)((x + .5) * width / targetWidth));
                var sy = Math.Min(height - 1, (int)((y + .5) * height / targetHeight));
                pixels.Slice((sy * width + sx) * 4, 4).CopyTo(output.AsSpan(target, 4));
                continue;
            }
            var left = (double)x * width / targetWidth; var right = (double)(x + 1) * width / targetWidth;
            var top = (double)y * height / targetHeight; var bottom = (double)(y + 1) * height / targetHeight;
            double red = 0, green = 0, blue = 0, alpha = 0, area = 0;
            for (var sy = (int)top; sy < Math.Ceiling(bottom); sy++)
            for (var sx = (int)left; sx < Math.Ceiling(right); sx++)
            {
                var weight = (Math.Min(sx + 1, right) - Math.Max(sx, left)) * (Math.Min(sy + 1, bottom) - Math.Max(sy, top));
                var source = (sy * width + sx) * 4; var a = pixels[source + 3] / 255.0;
                area += weight; alpha += a * weight;
                red += Decode(pixels[source], sRgb) * a * weight;
                green += Decode(pixels[source + 1], sRgb) * a * weight;
                blue += Decode(pixels[source + 2], sRgb) * a * weight;
            }
            output[target] = Encode(alpha > 0 ? red / alpha : 0, sRgb);
            output[target + 1] = Encode(alpha > 0 ? green / alpha : 0, sRgb);
            output[target + 2] = Encode(alpha > 0 ? blue / alpha : 0, sRgb);
            output[target + 3] = Encode(alpha / area, false);
        }
        return output;
    }

    private static double Decode(byte value, bool sRgb)
    {
        var normalized = value / 255.0;
        return !sRgb ? normalized : normalized <= .04045 ? normalized / 12.92 : Math.Pow((normalized + .055) / 1.055, 2.4);
    }
    private static byte Encode(double value, bool sRgb)
    {
        if (sRgb) value = value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1.0 / 2.4) - .055;
        return (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    }
}
