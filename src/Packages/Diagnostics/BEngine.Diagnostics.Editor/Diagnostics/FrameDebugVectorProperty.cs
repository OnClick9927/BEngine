using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Diagnostics;

public readonly record struct FrameDebugVectorProperty(string Name, NVector4 Value);
