using System.Globalization;
using BEngine.Serialization;

namespace BEngine.UIElements;

public readonly record struct UIElementRect(Fix64 X, Fix64 Y, Fix64 Width, Fix64 Height)
{
    public bool Contains(Vector2 point) => point.x >= X && point.x <= X + Width &&
                                           point.y >= Y && point.y <= Y + Height;
}
