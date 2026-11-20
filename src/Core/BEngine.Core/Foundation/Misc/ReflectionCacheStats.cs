namespace BEngine;

public readonly record struct ReflectionCacheStats(
    int Generation,
    int AssemblyCount,
    int TypeCount,
    long AssembliesScanned,
    long TypesScanned);
