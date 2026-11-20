namespace BEngine;

public readonly record struct ApplicationTick(long Frame, TimeSpan Elapsed, TimeSpan DeltaTime);
