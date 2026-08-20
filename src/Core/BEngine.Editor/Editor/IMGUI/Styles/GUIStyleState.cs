using BEngine.Rendering;

namespace BEngine.Editor;

public sealed class GUIStyleState
{
    public Color textColor { get; set; } = C(0.82f, 0.82f, 0.82f, 1);
    public Color backgroundColor { get; set; } = C(0.20f, 0.20f, 0.20f, 1);
    public Color borderColor { get; set; } = C(0, 0, 0, 0);

    private static Color C(float r, float g, float b, float a) => new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);
}
