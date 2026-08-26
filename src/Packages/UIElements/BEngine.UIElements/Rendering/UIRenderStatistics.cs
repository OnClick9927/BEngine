using BEngine.UIElements;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public readonly record struct UIRenderStatistics(
    int CommandCount,
    int BatchCount,
    int DrawCalls,
    int VertexCount,
    int UnresolvedImages);
