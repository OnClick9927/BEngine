namespace BEngine.UIElements;

public readonly record struct UIRenderCommand(
    UIRenderCommandType Type,
    VisualElement Element,
    UIElementRect Rect,
    UIElementRect ClipRect,
    UIColor Color,
    string Content = "",
    float FontSize = 0);
