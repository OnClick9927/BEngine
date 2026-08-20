using System.Numerics;

namespace BEngine;

public static class SortingLayer
{
    public const int MinimumIndex = 1;
    public const int MaximumIndex = 63;
    public const int UiLayerCount = 5;
    public const int UiBaseIndex = MaximumIndex - UiLayerCount + 1;

    public const ulong Default = 1UL << MinimumIndex;
    public const ulong Ui = 1UL << UiBaseIndex;
    public const ulong UiOverlay1 = 1UL << (UiBaseIndex + 1);
    public const ulong UiOverlay2 = 1UL << (UiBaseIndex + 2);
    public const ulong UiOverlay3 = 1UL << (UiBaseIndex + 3);
    public const ulong UiOverlay4 = 1UL << MaximumIndex;

    public static bool IsValid(ulong value) => value >= Default && (value & (value - 1)) == 0;
    public static bool IsWorld(ulong value) => IsValid(value) && value < Ui;
    public static bool IsUi(ulong value) => IsValid(value) && value >= Ui;

    public static ulong FromIndex(int index)
    {
        if (index is < MinimumIndex or > MaximumIndex)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"A sorting layer index must be between {MinimumIndex} and {MaximumIndex}.");
        return 1UL << index;
    }

    public static int IndexOf(ulong value)
    {
        if (!IsValid(value))
            throw new ArgumentOutOfRangeException(nameof(value),
                "A sorting layer value must be a power of two between 2 and ulong.MaxValue.");
        return BitOperations.TrailingZeroCount(value);
    }

    public static void Validate(ulong value, bool requireUi = false)
    {
        if (!IsValid(value) || requireUi && !IsUi(value))
            throw new ArgumentOutOfRangeException(nameof(value), requireUi
                ? $"UI sorting layers must be powers of two from 2^{UiBaseIndex} through 2^{MaximumIndex}."
                : "Sorting layers must be powers of two from 2^1 through 2^63.");
    }
}
