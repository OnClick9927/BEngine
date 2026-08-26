using BEngine.Rendering;

namespace BEngine.Editor;

public sealed class GUIStyleState
{
    [field: SerializeField]
    public Color textColor { get; set; } = C(0.82f, 0.82f, 0.82f, 1);
    [field: SerializeField]
    public Color backgroundColor { get; set; } = C(0.20f, 0.20f, 0.20f, 1);
    [field: SerializeField]
    public Color borderColor { get; set; } = C(0, 0, 0, 0);
    [field: SerializeField]
    public Texture? backgroundImage { get; set; }

    public GUIStyleState() { }

    public GUIStyleState(GUIStyleState other) => CopyFrom(other);

    public void CopyFrom(GUIStyleState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        textColor = other.textColor;
        backgroundColor = other.backgroundColor;
        borderColor = other.borderColor;
        backgroundImage = other.backgroundImage;
    }

    private static Color C(float r, float g, float b, float a) => new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);
}
