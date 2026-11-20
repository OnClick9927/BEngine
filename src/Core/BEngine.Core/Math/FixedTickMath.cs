namespace BEngine;

/// <summary>Ordered fixed point timer and scale operations for interpreted callers.</summary>
public static class FixedTickMath
{
    /// <summary>Decreases three timers without passing zero and reports the third timer's prior state.</summary>
    // Keep each checked subtraction and field write in caller order. The bool
    // is assigned only after the first two writes, including on exception paths.
    public static void AdvanceThree(ref Fix64 first,ref Fix64 second,ref Fix64 third,
        Fix64 delta,out bool thirdWasPositive)
    {
        first=Mathf.Max(0,first-delta);
        second=Mathf.Max(0,second-delta);
        thirdWasPositive=third>0;
        third=Mathf.Max(0,third-delta);
    }

    /// <summary>Decreases two timers without passing zero, in argument order.</summary>
    public static void AdvanceTwo(ref Fix64 first,ref Fix64 second,Fix64 delta)
    {
        first=Mathf.Max(0,first-delta);
        second=Mathf.Max(0,second-delta);
    }

    // Do not combine the scales: fixed point rounding/checked overflow makes
    // value*first*second different from value*(first*second).
    /// <summary>Applies two scales while retaining each intermediate rounding and overflow check.</summary>
    public static Vector2 ScaleInOrder(Vector2 value,Fix64 first,Fix64 second)
    {
        Fix64 x=value.x*first;
        Fix64 y=value.y*first;
        return new Vector2(x*second,y*second);
    }
}
