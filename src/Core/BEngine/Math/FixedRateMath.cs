namespace BEngine;

/// <summary>Preserves fixed-point evaluation order for common rate calculations.</summary>
public static class FixedRateMath
{
    public static Fix64 AddCappedQuotient(Fix64 value,Fix64 numerator,Fix64 denominator,Fix64 upperLimit)
        =>value+Mathf.Min(upperLimit,numerator/denominator);

    // Each optional factor is applied independently after the mandatory one.
    // Never multiply factors together: that changes Q32.32 rounding/overflow.
    public static Fix64 MultiplyFourInOrder(Fix64 value,Fix64 first,
        bool useSecond,Fix64 second,bool useThird,Fix64 third,bool useFourth,Fix64 fourth)
    {
        value*=first;
        if(useSecond)value*=second;
        if(useThird)value*=third;
        if(useFourth)value*=fourth;
        return value;
    }
}
