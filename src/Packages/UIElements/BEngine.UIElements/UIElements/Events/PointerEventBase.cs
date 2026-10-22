namespace BEngine.UIElements;

public enum UIPointerType { Mouse, Touch, Pen }

public abstract class PointerEventBase : UIEventBase
{
    /// <summary>-1 is the mouse; non-negative values identify independent touch contacts.</summary>
    public int pointerId { get; init; } = -1;
    public UIPointerType pointerType { get; init; }
    public Vector2 position { get; init; }
    public Fix64 pressure { get; init; }
}

public sealed class PointerCancelEvent : PointerEventBase;
public sealed class PointerCaptureLostEvent : PointerEventBase;
