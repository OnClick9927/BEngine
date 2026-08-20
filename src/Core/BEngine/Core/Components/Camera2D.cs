namespace BEngine;

[AddComponentMenu("Rendering/Camera 2D")]
[DisallowMultipleComponent]
public sealed class Camera2D : Behaviour
{
    private Fix64 _size = 5;

    public Fix64 size
    {
        get { MainThreadGuard.Ensure(); return _size; }
        set { MainThreadGuard.Ensure(); _size = Fix64.Max(Fix64.Parse("0.001"), value); }
    }

    public Color backgroundColor { get; set; } = new(
        Fix64.Parse("0.055"), Fix64.Parse("0.071"), Fix64.Parse("0.09"), 1);
    public bool isMain { get; set; } = true;
    public Fix64 depth { get; set; }
}
