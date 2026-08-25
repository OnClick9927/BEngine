namespace BEngine;

[AddComponentMenu("Rendering/Camera 2D")]
[DisallowMultipleComponent]
public sealed class Camera2D : Behaviour
{
    private static readonly Fix64 MinimumViewportSize = Fix64.Parse("0.001");

    public Fix64 size
    {
        get;
        set => field = Fix64.Max(MinimumViewportSize, value);
    } = 5;

    public Color backgroundColor { get; set; } = new(
        Fix64.Parse("0.055"), Fix64.Parse("0.071"), Fix64.Parse("0.09"), 1);
    public bool isMain { get; set; } = true;
    [FormerlySerializedAs("depth")]
    public Fix64 priority { get; set; }
    public CameraClearMode clearMode { get; set; } = CameraClearMode.Color;
    public ulong cullingMask
    {
        get;
        set => field = value & SortingLayer.AllMask;
    } = SortingLayer.AllMask;

    public Rect viewportRect
    {
        get;
        set
        {
            var x = Fix64.Clamp(value.x, Fix64.Zero, Fix64.One - MinimumViewportSize);
            var y = Fix64.Clamp(value.y, Fix64.Zero, Fix64.One - MinimumViewportSize);
            field = new Rect(
                x,
                y,
                Fix64.Clamp(value.width, MinimumViewportSize, Fix64.One - x),
                Fix64.Clamp(value.height, MinimumViewportSize, Fix64.One - y));
        }
    } = new(0, 0, 1, 1);
}
