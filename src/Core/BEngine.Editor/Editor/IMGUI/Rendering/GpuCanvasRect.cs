using System.Runtime.InteropServices;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Rendering;

public readonly record struct GpuCanvasRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;
    public static GpuCanvasRect Intersect(GpuCanvasRect left, GpuCanvasRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottomEdge = Math.Min(left.Bottom, right.Bottom);
        return new GpuCanvasRect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottomEdge - y));
    }
}
