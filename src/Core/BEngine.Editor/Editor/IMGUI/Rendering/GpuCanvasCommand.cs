using System.Runtime.InteropServices;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Rendering;

public readonly record struct GpuCanvasCommand(
    GpuCanvasCommandType Type,
    GpuCanvasRect Rect,
    GpuCanvasRect ClipRect,
    GpuCanvasColor Color,
    string Content = "",
    float FontSize = 13,
    string FontFamily = "",
    GpuCanvasColor Color2 = default,
    GpuCanvasColor Color3 = default,
    GpuCanvasColor Color4 = default);
