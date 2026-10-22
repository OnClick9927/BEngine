using System.Runtime.InteropServices;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Rendering;

public readonly record struct GpuCanvasColor(byte R, byte G, byte B, byte A = 255)
{
    public static GpuCanvasColor FromColor(Color color) => new(
        (byte)Math.Clamp((int)((float)color.r * 255), 0, 255),
        (byte)Math.Clamp((int)((float)color.g * 255), 0, 255),
        (byte)Math.Clamp((int)((float)color.b * 255), 0, 255),
        (byte)Math.Clamp((int)((float)color.a * 255), 0, 255));
}
