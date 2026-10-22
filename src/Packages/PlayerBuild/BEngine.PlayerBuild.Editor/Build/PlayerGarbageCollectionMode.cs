namespace BEngine.Editor;

/// <summary>Optional runtime GC policy selected when publishing a Player.</summary>
public enum PlayerGarbageCollectionMode
{
    RuntimeDefault,
    Workstation,
    Server
}

public static class PlayerGarbageCollectionBuildSettings
{
    /// <summary>Zero heaps preserves the runtime default; an explicit count requires Server GC.</summary>
    public static void ValidateWindows(PlayerGarbageCollectionMode mode, int heapCount)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (heapCount < 0)
            throw new ArgumentOutOfRangeException(nameof(heapCount), "Windows GC heap count cannot be negative.");
        if (heapCount != 0 && mode != PlayerGarbageCollectionMode.Server)
            throw new ArgumentException("An explicit Windows GC heap count requires Server GC.", nameof(heapCount));
    }
}
