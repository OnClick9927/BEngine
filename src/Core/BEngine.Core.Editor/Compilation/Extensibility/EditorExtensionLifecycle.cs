namespace BEngine.Editor;

/// <summary>Invalidates extension-owned reflection caches after compilation or package changes.</summary>
public static class EditorExtensionLifecycle
{
    public static event Action? Invalidated;
    public static void Invalidate() => Invalidated?.Invoke();
    /// <summary>Synchronizes reflected editor queries and registered warmups after an assembly
    /// registration, preference or retirement has completed. Does not own, load or unload assemblies,
    /// execute game initialization, or select a package. Call after failed preparation cleanup too.</summary>
    public static void SynchronizeReflection() => TypeCache.Refresh();
}
