namespace BEngine;

public readonly record struct BFrameTick(long Frame, TimeSpan Elapsed, TimeSpan DeltaTime);
