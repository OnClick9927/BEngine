using System.Globalization;
using BEngine.Serialization;

namespace BEngine.UIElements;

public sealed class PanelSettings : ScriptableObject
{
    public PanelScaleMode scaleMode { get; set; } = PanelScaleMode.ScaleWithScreenSize;
    public int referenceWidth { get; set; } = 1280;
    public int referenceHeight { get; set; } = 720;

    public Fix64 ResolveScale(int width, int height)
    {
        if (scaleMode == PanelScaleMode.ConstantPixelSize) return Fix64.One;
        var widthScale = (Fix64)Math.Max(1, width) / Math.Max(1, referenceWidth);
        var heightScale = (Fix64)Math.Max(1, height) / Math.Max(1, referenceHeight);
        return (widthScale + heightScale) / 2;
    }
}
