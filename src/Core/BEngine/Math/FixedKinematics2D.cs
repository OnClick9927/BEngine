namespace BEngine;

/// <summary>Ordered fixed-point clock and planar displacement operations.</summary>
public static class FixedKinematics2D
{
    public static Fix64 AdvanceAndMeasure(ref Fix64 clock,Fix64 deltaTime,
        Vector2 target,Vector2 position,out Vector2 difference)
    {
        clock-=deltaTime;
        difference=target-position;
        return difference.magnitude;
    }
}
