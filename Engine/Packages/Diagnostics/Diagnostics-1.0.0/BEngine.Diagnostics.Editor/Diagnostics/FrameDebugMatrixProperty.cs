using NMatrix4x4 = System.Numerics.Matrix4x4;

namespace BEngine.Editor.Diagnostics;

public readonly record struct FrameDebugMatrixProperty(string Name, NMatrix4x4 Value);
