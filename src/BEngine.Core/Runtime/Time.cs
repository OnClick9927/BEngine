namespace BEngine;

public static class Time
{
    public static Fix64 deltaTime { get; internal set; }
    public static Fix64 fixedDeltaTime { get; set; } = Fix64.Parse("0.02");
    public static Fix64 time { get; internal set; }
    public static long frameCount { get; internal set; }

    internal static void Reset()
    {
        deltaTime = Fix64.Zero;
        time = Fix64.Zero;
        frameCount = 0;
    }
}
