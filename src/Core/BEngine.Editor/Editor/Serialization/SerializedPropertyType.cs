using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public enum SerializedPropertyType
{
    Generic,
    Integer,
    Boolean,
    Float,
    String,
    Color,
    ObjectReference,
    LayerMask,
    Enum,
    Vector2,
    Vector4,
    Rect
}
