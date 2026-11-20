namespace BEngine;

/// <summary>Lifetime counters. Destroyed counts disposal attempts, even if a destroy callback fails.</summary>
public readonly record struct PoolStatistics(
    int CountActive,
    int CountInactive,
    int PeakActive,
    long TotalCreated,
    long TotalGets,
    long TotalReused,
    long TotalReleased,
    long TotalDestroyed);
