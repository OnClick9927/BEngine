using System.Runtime.InteropServices;

namespace BEngine.Editor;

[StructLayout(LayoutKind.Sequential)]
internal struct EditorNativePoint
{
    internal int X;
    internal int Y;
}
