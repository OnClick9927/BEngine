namespace BEngine;

/// <summary>Layer values are one-based natural indices. Masks are created explicitly via <see cref="ToMask"/>.</summary>
public static class SortingLayer
{
    public const int MinimumIndex = 1;
    public const int MaximumIndex = 63;
    public const int BuiltInLayerCount = 5;

    public static ulong Default => SortingLayerRegistry.ValueOfBuiltIn(SortingLayerRegistry.DefaultId);
    public static ulong TransparentFx => SortingLayerRegistry.ValueOfBuiltIn(SortingLayerRegistry.TransparentFxId);
    public static ulong IgnoreRaycast => SortingLayerRegistry.ValueOfBuiltIn(SortingLayerRegistry.IgnoreRaycastId);
    public static ulong Water => SortingLayerRegistry.ValueOfBuiltIn(SortingLayerRegistry.WaterId);
    public static ulong Ui => SortingLayerRegistry.ValueOfBuiltIn(SortingLayerRegistry.UiId);

    // Compatibility names. Projects may define these optional UI layers.
    public const ulong UiOverlay1 = 6;
    public const ulong UiOverlay2 = 7;
    public const ulong UiOverlay3 = 8;
    public const ulong UiOverlay4 = 9;
    public const int UiLayerCount = 1;
    public const int LegacyMappedUiIndex = 5;
    public static int UiBaseIndex => IndexOf(Ui);
    public const ulong AllMask = ulong.MaxValue >> 1;

    public static bool IsValid(ulong value) => value is >= MinimumIndex and <= MaximumIndex;
    public static bool IsWorld(ulong value) => IsValid(value) && !IsUi(value);
    public static bool IsUi(ulong value) => IsValid(value) && SortingLayerRegistry.IsUi(value);

    public static ulong FromIndex(int index)
    {
        if (index is < MinimumIndex or > MaximumIndex)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"A layer index must be between {MinimumIndex} and {MaximumIndex}.");
        return (ulong)index;
    }

    public static int IndexOf(ulong value)
    {
        Validate(value);
        return checked((int)value);
    }

    public static ulong ToMask(ulong layer)
    {
        Validate(layer);
        return 1UL << (IndexOf(layer) - MinimumIndex);
    }

    public static ulong FromLegacyPowerOfTwo(ulong value)
    {
        if (value < 2 || (value & (value - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(value),
                "A legacy layer value must be one of 2^1 through 2^63.");
        return FromIndex(MapLegacyIndex(System.Numerics.BitOperations.TrailingZeroCount(value)));
    }

    public static ulong FromLegacyMask(ulong mask)
    {
        var result = 0UL;
        for (var legacyIndex = MinimumIndex; legacyIndex <= MaximumIndex; legacyIndex++)
        {
            var legacyBit = 1UL << legacyIndex;
            if ((mask & legacyBit) == 0) continue;
            result |= ToMask(FromIndex(MapLegacyIndex(legacyIndex)));
        }
        return result;
    }

    internal static int MapLegacyIndex(int legacyIndex) => legacyIndex switch
    {
        <= 4 => legacyIndex,
        59 => LegacyMappedUiIndex,
        <= 58 => legacyIndex + 1,
        _ => legacyIndex
    };

    public static void Validate(ulong value, bool requireUi = false)
    {
        if (!IsValid(value) || requireUi && !IsUi(value))
            throw new ArgumentOutOfRangeException(nameof(value), requireUi
                ? "The value must identify a configured UI layer."
                : $"Layer values must be natural indices from {MinimumIndex} through {MaximumIndex}.");
    }
}
