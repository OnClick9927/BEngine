namespace BEngine.Editor;

/// <summary>Pure state model used by native menu hooks and headless regression tests.</summary>
internal sealed class NativeMenuMouseExitPolicy(bool startsInsideOwner = false)
{
    private bool _hasEnteredOwnedRegion = startsInsideOwner;
    private bool _cancelled;

    internal bool Observe(bool insideNativeMenu, bool insideOwnerRegion)
    {
        if (_cancelled) return false;
        if (insideNativeMenu || insideOwnerRegion)
        {
            _hasEnteredOwnedRegion = true;
            return false;
        }
        if (!_hasEnteredOwnedRegion) return false;
        _cancelled = true;
        return true;
    }
}
