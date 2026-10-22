namespace BEngine.Cinemachine;

public readonly record struct CameraState2D(
    Vector2 RawPosition,
    Fix64 RawRotation,
    Fix64 OrthographicSize,
    Vector2 PositionCorrection,
    Fix64 RotationCorrection,
    Vector2 ReferenceLookAt,
    bool HasLookAt)
{
    public Vector2 FinalPosition => RawPosition + PositionCorrection;
    public Fix64 FinalRotation => RawRotation + RotationCorrection;

    public static CameraState2D Default => new(
        Vector2.zero, Fix64.Zero, 5, Vector2.zero, Fix64.Zero, Vector2.zero, false);

    public static CameraState2D Lerp(CameraState2D from, CameraState2D to, Fix64 time)
    {
        var amount = Mathf.Clamp01(time);
        return new CameraState2D(
            Vector2.Lerp(from.RawPosition, to.RawPosition, amount),
            LerpAngle(from.RawRotation, to.RawRotation, amount),
            Mathf.Lerp(from.OrthographicSize, to.OrthographicSize, amount),
            Vector2.Lerp(from.PositionCorrection, to.PositionCorrection, amount),
            LerpAngle(from.RotationCorrection, to.RotationCorrection, amount),
            Vector2.Lerp(from.ReferenceLookAt, to.ReferenceLookAt, amount),
            from.HasLookAt && to.HasLookAt);
    }

    private static Fix64 LerpAngle(Fix64 from, Fix64 to, Fix64 time)
    {
        var delta = Mathf.Repeat(to - from + 180, 360) - 180;
        return from + delta * time;
    }
}
