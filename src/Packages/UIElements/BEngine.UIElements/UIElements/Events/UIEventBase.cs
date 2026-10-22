namespace BEngine.UIElements;

public abstract class UIEventBase
{
    public VisualElement? target { get; internal set; }
    public VisualElement? currentTarget { get; internal set; }
    public bool propagationStopped { get; private set; }
    public bool immediatePropagationStopped { get; private set; }

    public void StopPropagation() => propagationStopped = true;

    public void StopImmediatePropagation()
    {
        immediatePropagationStopped = true;
        propagationStopped = true;
    }
}
