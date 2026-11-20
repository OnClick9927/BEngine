namespace BEngine;

/// <summary>Owner-thread cache slot. The weak reference and recursive-load guard remain private to Core.</summary>
public readonly struct AssetLoadSlot : IEquatable<AssetLoadSlot>
{
    private readonly AssetCacheEntry? _entry;
    private AssetCacheEntry Entry => _entry ?? throw new InvalidOperationException("The asset load slot is uninitialized.");
    private AssetLoadSlot(AssetCacheEntry entry) => _entry = entry;
    public static AssetLoadSlot Create() => new(new AssetCacheEntry());
    public bool IsLoading => Entry.IsLoading;
    public bool TryGetTarget(out BAsset asset) => Entry.TryGetTarget(out asset);
    public BAsset? GetOrLoad(Func<BAsset?> loader) => Entry.GetOrLoad(loader);
    public BAsset? GetOrLoad<TState>(TState state, Func<TState, BAsset?> loader) => Entry.GetOrLoad(state, loader);
    public bool Equals(AssetLoadSlot other) => ReferenceEquals(_entry, other._entry);
    public override bool Equals(object? value) => value is AssetLoadSlot other && Equals(other);
    public override int GetHashCode() => _entry?.GetHashCode() ?? 0;
}
