using System.Runtime.InteropServices;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Rendering;

public enum GpuCanvasCommandType
{
    SolidRect,
    GradientRect,
    Text,
    Image
}
