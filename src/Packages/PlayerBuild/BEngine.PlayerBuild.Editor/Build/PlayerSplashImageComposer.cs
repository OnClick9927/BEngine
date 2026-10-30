using BEngine.Rendering;

namespace BEngine.Editor;

/// <summary>Build-time branding shared by every Player host, with no runtime composition cost.</summary>
internal static class PlayerSplashImageComposer
{
    internal static byte[] AddEngineBadge(byte[] logoPng, byte[] enginePng, byte[]? badgePng = null)
    {
        if (!PngImageCodec.TryDecode(logoPng, out var width, out var height, out var logo) ||
            !PngImageCodec.TryDecode(enginePng, out var engineWidth, out var engineHeight, out var engine))
            throw new InvalidDataException("The Player logo or BEngine branding icon is not a supported PNG.");
        // An explicitly selected copy of the default engine icon is still the default logo.
        if (width == engineWidth && height == engineHeight && logo.AsSpan().SequenceEqual(engine))
            return logoPng;

        // Branding has its own tightly cropped, transparent artwork. The default
        // splash image can retain breathing room without shrinking the visible badge.
        if (badgePng is not null &&
            !PngImageCodec.TryDecode(badgePng, out engineWidth, out engineHeight, out engine))
            throw new InvalidDataException("The BEngine splash badge is not a supported PNG.");

        var badgeWidth = Math.Max(1, (int)Math.Round(width / 5d));
        var badgeHeight = Math.Max(1, (int)Math.Round((double)badgeWidth * engineHeight / engineWidth));
        byte[] badge;
        if (badgeWidth <= engineWidth && badgeHeight <= engineHeight)
            badge = TexturePixelProcessing.ResizeRgba(engine, engineWidth, engineHeight, badgeWidth, badgeHeight);
        else
        {
            badge = new byte[checked(badgeWidth * badgeHeight * 4)];
            for (var y = 0; y < badgeHeight; y++)
            for (var x = 0; x < badgeWidth; x++)
                engine.AsSpan(((y * engineHeight / badgeHeight) * engineWidth + x * engineWidth / badgeWidth) * 4, 4)
                    .CopyTo(badge.AsSpan((y * badgeWidth + x) * 4, 4));
        }

        // Keep the outline away from the application's rounded corner and border.
        // Clamp the inset for tiny logos; preserve the 1/5 badge width.
        var inset = Math.Min(Math.Max(1, (int)Math.Round(Math.Min(width, height) / 20d)),
            (width - badgeWidth) / 2);
        // Very wide logos need transparent headroom so the badge remains whole.
        var outputHeight = Math.Max(height, badgeHeight + 2 * inset);
        var output = new byte[checked(width * outputHeight * 4)];
        logo.CopyTo(output, (outputHeight - height) * width * 4);
        for (var y = 0; y < badgeHeight; y++)
        for (var x = 0; x < badgeWidth; x++)
        {
            var source = (y * badgeWidth + x) * 4;
            var target = ((outputHeight - badgeHeight - inset + y) * width + width - badgeWidth - inset + x) * 4;
            var alpha = badge[source + 3];
            if (alpha == 0) continue;
            var destinationAlpha = output[target + 3];
            var combined = alpha * 255 + destinationAlpha * (255 - alpha);
            for (var channel = 0; channel < 3; channel++)
                output[target + channel] = (byte)((badge[source + channel] * alpha * 255 +
                    output[target + channel] * destinationAlpha * (255 - alpha) + combined / 2) / combined);
            output[target + 3] = (byte)((combined + 127) / 255);
        }
        return PngImageCodec.EncodeRgba(width, outputHeight, output);
    }
}
