using System.Globalization;
using System.Numerics;

namespace BEngine;

/// <summary>Signed deterministic Q32.32 fixed-point number.</summary>
public readonly struct Fix64 : IComparable<Fix64>, IEquatable<Fix64>, IFormattable, ISpanParsable<Fix64>
{
    public const int FractionalBits = 32;
    public const long OneRaw = 1L << FractionalBits;

    public static readonly Fix64 Zero = FromRaw(0);
    public static readonly Fix64 One = FromRaw(OneRaw);
    public static readonly Fix64 Half = FromRaw(OneRaw / 2);
    public static readonly Fix64 Pi = Parse("3.14159265358979323846");
    public static readonly Fix64 TwoPi = Pi * 2;
    public static readonly Fix64 HalfPi = Pi / 2;
    public static readonly Fix64 Deg2Rad = Pi / 180;
    public static readonly Fix64 Rad2Deg = 180 / Pi;
    public static readonly Fix64 Epsilon = FromRaw(1);

    public long RawValue { get; }

    private Fix64(long rawValue) => RawValue = rawValue;

    public static Fix64 FromRaw(long rawValue) => new(rawValue);
    private static readonly ulong[] DecimalPowersOfTen =
    [
        1UL, 10UL, 100UL, 1000UL, 10000UL, 100000UL, 1000000UL,
        10000000UL, 100000000UL, 1000000000UL, 10000000000UL,
        100000000000UL, 1000000000000UL, 10000000000000UL,
        100000000000000UL, 1000000000000000UL, 10000000000000000UL,
        100000000000000000UL, 1000000000000000000UL, 10000000000000000000UL
    ];

    public static Fix64 FromDecimal(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        // A 32-bit coefficient times 2^32 fits exactly in both UInt64 and
        // Decimal's 96-bit coefficient. No decimal rounding is lost here.
        if ((bits[1] | bits[2]) == 0)
        {
            int scale = (bits[3] >> 16) & 0xff;
            if (scale >= 20) return Zero;
            ulong magnitude = (ulong)(uint)bits[0] << FractionalBits;
            if (scale != 0) magnitude /= DecimalPowersOfTen[scale];
            bool negative = bits[3] < 0;
            if (magnitude > (negative ? 1UL << 63 : (ulong)long.MaxValue))
                throw new OverflowException();
            return FromRaw(unchecked((long)(negative ? 0UL - magnitude : magnitude)));
        }
        return FromRaw(decimal.ToInt64(value * OneRaw));
    }

    /// <summary>Converts elapsed time to fixed-point seconds without a floating-point intermediate.</summary>
    public static Fix64 FromTimeSpan(TimeSpan value)
    {
        var ticks = value.Ticks;
        return ticks == unchecked((int)ticks)
            ? FromRaw((ticks << FractionalBits) / TimeSpan.TicksPerSecond)
            : FromRaw(checked((long)((Int128)ticks * OneRaw / TimeSpan.TicksPerSecond)));
    }

    public static Fix64 Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Parse(value.AsSpan(), CultureInfo.InvariantCulture);
    }

    public static Fix64 Parse(string value, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Parse(value.AsSpan(), provider);
    }

    public static Fix64 Parse(ReadOnlySpan<char> value) =>
        Parse(value, CultureInfo.InvariantCulture);

    public static Fix64 Parse(ReadOnlySpan<char> value, IFormatProvider? provider)
    {
        _ = provider; // Fix64 text is deliberately culture-invariant for deterministic assets.
        var original = value;
        var text = value.Trim();
        if (text.Length == 0)
        {
            throw new FormatException("A fixed-point value cannot be empty.");
        }

        var exponent = 0;
        var exponentIndex = text.IndexOfAny('e', 'E');
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }
        if (text.IsEmpty) throw CreateFormatException(original);

        var negative = text[0] == '-';
        if (negative || text[0] == '+')
        {
            text = text[1..];
        }

        var decimalIndex = text.IndexOf('.');
        if (decimalIndex != text.LastIndexOf('.'))
        {
            throw CreateFormatException(original);
        }

        var fractionalDigits = decimalIndex < 0 ? 0 : text.Length - decimalIndex - 1;
        Span<char> digits = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        var digitCount = 0;
        foreach (var character in text)
        {
            if (character == '.') continue;
            if (!char.IsAsciiDigit(character)) throw CreateFormatException(original);
            digits[digitCount++] = character;
        }
        if (digitCount == 0) throw CreateFormatException(original);

        var significantDigits = digits[..digitCount].TrimStart('0');
        if (significantDigits.Length == 0)
        {
            return Zero;
        }

        var scale = (long)fractionalDigits - exponent;
        var integerDigits = significantDigits.Length - scale;
        if (integerDigits > 10) throw CreateOverflowException(original);
        if (integerDigits <= -10) return Zero;

        ulong integer = 0;
        for (var index = 0; index < integerDigits; index++)
            integer = integer * 10 + (index < significantDigits.Length ? (uint)(significantDigits[index] - '0') : 0);
        if (integer > 2147483648UL) throw CreateOverflowException(original);

        // Every Q32.32 boundary has an exact decimal expansion of at most 32
        // fractional digits. Digits after that cannot cross a boundary, so a
        // bounded prefix gives the same truncation as arbitrary-precision math.
        UInt128 numerator = 0;
        UInt128 denominator = 1;
        var fractionLength = (int)Math.Min(FractionalBits, Math.Max(0, scale));
        for (var index = 0; index < fractionLength; index++)
        {
            var source = integerDigits + index;
            numerator = numerator * 10 + (source >= 0 && source < significantDigits.Length
                ? (uint)(significantDigits[(int)source] - '0') : 0);
            denominator *= 10;
        }

        uint fractional = 0;
        if (fractionLength <= 9 && numerator != 0)
        {
            // Common asset values fit in a single 64-bit division.
            fractional = (uint)(((ulong)numerator << FractionalBits) / (ulong)denominator);
        }
        else if (numerator != 0)
        {
            // Keep the intermediate below 2 * 10^32, which fits in UInt128.
            for (var bit = 0; bit < FractionalBits; bit++)
            {
                numerator <<= 1;
                fractional <<= 1;
                if (numerator < denominator) continue;
                numerator -= denominator;
                fractional |= 1;
            }
        }
        var raw = (integer << FractionalBits) | fractional;
        if (raw > (negative ? 1UL << 63 : (ulong)long.MaxValue))
            throw CreateOverflowException(original);
        return FromRaw(unchecked((long)(negative ? 0UL - raw : raw)));
    }

    private static OverflowException CreateOverflowException(ReadOnlySpan<char> value) =>
        new($"'{value.ToString()}' is outside the Fix64 range.");

    public static bool TryParse(string? value, out Fix64 result)
        => TryParse(value, CultureInfo.InvariantCulture, out result);

    public static bool TryParse(string? value, IFormatProvider? provider, out Fix64 result)
    {
        if (value is null)
        {
            result = Zero;
            return false;
        }
        return TryParse(value.AsSpan(), provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<char> value, out Fix64 result) =>
        TryParse(value, CultureInfo.InvariantCulture, out result);

    public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out Fix64 result)
    {
        try
        {
            result = Parse(value, provider);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentNullException or FormatException or OverflowException)
        {
            result = Zero;
            return false;
        }
    }

    private static FormatException CreateFormatException(ReadOnlySpan<char> value) =>
        new($"'{value.ToString()}' is not a valid fixed-point value.");

    public static implicit operator Fix64(int value) => FromRaw((long)value << FractionalBits);
    public static implicit operator Fix64(long value)
    {
        if (value != unchecked((int)value)) throw new OverflowException();
        return FromRaw(value << FractionalBits);
    }
    public static explicit operator Fix64(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        var exponent = (int)((bits >> 23) & 0xff);
        if (exponent == 0xff) throw new OverflowException("A non-finite value cannot be converted to Fix64.");
        var significand = (bits & 0x7fffff) | (exponent == 0 ? 0u : 1u << 23);
        return FromBinary(significand, (exponent == 0 ? 1 : exponent) - 127 - 23 + FractionalBits,
            (bits & 0x80000000) != 0);
    }
    public static explicit operator Fix64(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff);
        if (exponent == 0x7ff) throw new OverflowException("A non-finite value cannot be converted to Fix64.");
        var significand = (bits & 0xfffffffffffff) | (exponent == 0 ? 0UL : 1UL << 52);
        return FromBinary(significand, (exponent == 0 ? 1 : exponent) - 1023 - 52 + FractionalBits,
            (bits & 0x8000000000000000) != 0);
    }

    private static Fix64 FromBinary(ulong significand, int shift, bool negative)
    {
        if (significand == 0) return Zero;
        // Scale the exact IEEE significand, truncating toward zero just like decimal conversion.
        // Validate before shifting: CLR integer shifts mask the shift count even in checked code.
        ulong magnitude;
        var limit = negative ? 1UL << 63 : (ulong)long.MaxValue;
        if (shift >= 0)
        {
            if (shift >= 64 || significand > (limit >> shift))
                throw new OverflowException("The value is outside the Fix64 range.");
            magnitude = significand << shift;
        }
        else
        {
            magnitude = shift <= -64 ? 0 : significand >> -shift;
        }
        if (magnitude > limit) throw new OverflowException("The value is outside the Fix64 range.");
        return FromRaw(unchecked((long)(negative ? 0UL - magnitude : magnitude)));
    }
    public static explicit operator int(Fix64 value) => (int)(value.RawValue >> FractionalBits);
    public static explicit operator long(Fix64 value) => value.RawValue >> FractionalBits;
    public static explicit operator float(Fix64 value) => (float)((double)value.RawValue / OneRaw);
    public static explicit operator double(Fix64 value) => (double)value.RawValue / OneRaw;
    public static explicit operator decimal(Fix64 value) => (decimal)value.RawValue / OneRaw;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator +(Fix64 left, Fix64 right) => FromRaw(checked(left.RawValue + right.RawValue));
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator -(Fix64 left, Fix64 right) => FromRaw(checked(left.RawValue - right.RawValue));
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator -(Fix64 value) => FromRaw(checked(-value.RawValue));
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator *(Fix64 left, Fix64 right)
    {
        // Keep all 128 product bits, including negative products' arithmetic
        // rounding, without constructing several Int128 values in the interpreter.
        var high = Math.BigMul(left.RawValue, right.RawValue, out var low);
        _ = checked((int)high);
        return FromRaw(unchecked((long)(((ulong)high << FractionalBits) |
            ((ulong)low >> FractionalBits))));
    }
    public static Fix64 operator /(Fix64 left, Fix64 right)
    {
        if (right.RawValue == 0)
        {
            throw new DivideByZeroException();
        }

        // Exact integer divisors cancel the Q32.32 scale. Small numerators can
        // be shifted in 64 bits. Both paths retain division toward zero and
        // the CLR's checked MinValue / -1 overflow behavior.
        if ((right.RawValue & (OneRaw - 1)) == 0)
            return FromRaw(left.RawValue / (right.RawValue >> FractionalBits));
        if (left.RawValue == unchecked((int)left.RawValue))
            return FromRaw((left.RawValue << FractionalBits) / right.RawValue);
        if (OperatingSystem.IsBrowser() && TryDivideBrowser(left.RawValue, right.RawValue, out var quotient))
            return FromRaw(quotient);
        return FromRaw(checked((long)(((Int128)left.RawValue << FractionalBits) / right.RawValue)));
    }

    // Wasm implements Int128 division in software. A double supplies only a
    // bounded quotient estimate; the unsigned 96-bit dividend and exact 128-bit
    // product below prove the result. A rejected estimate uses the original
    // division, including its overflow and truncation behavior.
    private static bool TryDivideBrowser(long numerator, long denominator, out long quotient)
    {
        var dividend = numerator < 0 ? unchecked(0UL - (ulong)numerator) : (ulong)numerator;
        var divisor = denominator < 0 ? unchecked(0UL - (ulong)denominator) : (ulong)denominator;
        var estimate = (double)dividend * OneRaw / divisor;
        if (estimate >= 9007199254740992.0) { quotient = 0; return false; }

        var magnitude = (ulong)estimate;
        var dividendHigh = dividend >> FractionalBits;
        var dividendLow = dividend << FractionalBits;
        var productHigh = Math.BigMul(magnitude, divisor, out var productLow);
        if (productHigh > dividendHigh || productHigh == dividendHigh && productLow > dividendLow)
        {
            // Correct one unit down, then prove this product no longer exceeds
            // the dividend. Larger estimate errors take the exact fallback.
            magnitude--;
            if (productLow < divisor) productHigh--;
            productLow = unchecked(productLow - divisor);
            if (productHigh > dividendHigh || productHigh == dividendHigh && productLow > dividendLow)
            { quotient = 0; return false; }
        }

        var remainderHigh = unchecked(dividendHigh - productHigh - (dividendLow < productLow ? 1UL : 0UL));
        var remainderLow = unchecked(dividendLow - productLow);
        if (remainderHigh != 0 || remainderLow >= divisor)
        {
            // Correct one unit up. A remainder strictly below the divisor is
            // the exact floor proof; no floating-point rounding is retained.
            magnitude++;
            if (remainderLow < divisor) remainderHigh--;
            remainderLow = unchecked(remainderLow - divisor);
            if (remainderHigh != 0 || remainderLow >= divisor)
            { quotient = 0; return false; }
        }
        quotient = (numerator ^ denominator) < 0 ? -(long)magnitude : (long)magnitude;
        return true;
    }

    public static Fix64 operator %(Fix64 left, Fix64 right) => FromRaw(left.RawValue % right.RawValue);
    public static Fix64 operator ++(Fix64 value) => value + One;
    public static Fix64 operator --(Fix64 value) => value - One;
    public static bool operator ==(Fix64 left, Fix64 right) => left.RawValue == right.RawValue;
    public static bool operator !=(Fix64 left, Fix64 right) => left.RawValue != right.RawValue;
    public static bool operator <(Fix64 left, Fix64 right) => left.RawValue < right.RawValue;
    public static bool operator >(Fix64 left, Fix64 right) => left.RawValue > right.RawValue;
    public static bool operator <=(Fix64 left, Fix64 right) => left.RawValue <= right.RawValue;
    public static bool operator >=(Fix64 left, Fix64 right) => left.RawValue >= right.RawValue;

    public static Fix64 Abs(Fix64 value) => value.RawValue < 0 ? -value : value;
    public static Fix64 Min(Fix64 left, Fix64 right) => left < right ? left : right;
    public static Fix64 Max(Fix64 left, Fix64 right) => left > right ? left : right;
    public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max) => Min(Max(value, min), max);

    public static Fix64 Sqrt(Fix64 value)
    {
        if (value < Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Square root requires a non-negative value.");
        }

        if (value == Zero)
        {
            return Zero;
        }

        var input = (ulong)value.RawValue;
        // Floating point supplies only a starting guess. Exact 96-bit integer
        // comparisons determine floor(sqrt(input << 32)), retaining identical
        // results even when the initial square root rounds to either neighbor.
        var root = (ulong)(Math.Sqrt(input) * 65536.0);
        var high = input >> FractionalBits;
        var low = input << FractionalBits;
        while (SquareExceeds(root, high, low)) root--;
        while (!SquareExceeds(root + 1, high, low)) root++;
        return FromRaw((long)root);
    }

    private static bool SquareExceeds(ulong root, ulong high, ulong low)
    {
        var squareHigh = Math.BigMul(root, root, out var squareLow);
        return squareHigh > high || (squareHigh == high && squareLow > low);
    }

    // Each thread retains at most 1024 exact folded-angle results (24 KiB).
    [ThreadStatic] private static SinCacheEntry[]? _sinCache;

    private struct SinCacheEntry
    {
        internal long Key;
        internal long Value;
        internal bool Valid;
    }

    public static Fix64 Sin(Fix64 radians)
    {
        var x = NormalizeRadians(radians);
        if (x == Zero || x == Pi || x == -Pi) return Zero;
        if (x == HalfPi) return One;
        if (x == -HalfPi) return -One;
        // Fold into [-pi/2, pi/2] before approximation, keeping all arithmetic deterministic.
        if (x > HalfPi) x = Pi - x;
        else if (x < -HalfPi) x = -Pi - x;
        SinCacheEntry[]? cache = null;
        int slot = 0;
        if (OperatingSystem.IsBrowser())
        {
            cache = _sinCache ??= new SinCacheEntry[1024];
            long raw = x.RawValue;
            slot = (int)(raw ^ (raw >> 13) ^ (raw >> 26)) & 1023;
            ref var entry = ref cache[slot];
            if (entry.Valid && entry.Key == raw) return FromRaw(entry.Value);
        }
        var x2 = x * x;
        var term = x;
        var result = term;
        term *= -x2 / (2 * 3);
        result += term;
        term *= -x2 / (4 * 5);
        result += term;
        term *= -x2 / (6 * 7);
        result += term;
        term *= -x2 / (8 * 9);
        result += term;
        term *= -x2 / (10 * 11);
        result += term;
        term *= -x2 / (12 * 13);
        result += term;
        term *= -x2 / (14 * 15);
        var value = result + term;
        if (cache is not null)
        {
            ref var entry = ref cache[slot];
            entry.Key = x.RawValue;
            entry.Value = value.RawValue;
            entry.Valid = true;
        }
        return value;
    }

    public static Fix64 Hypot(Fix64 x, Fix64 y)
    {
        x = Abs(x); y = Abs(y);
        var largest = Max(x, y);
        if (largest == Zero) return Zero;
        x /= largest; y /= largest;
        return largest * Sqrt(x * x + y * y);
    }

    public static Fix64 Cos(Fix64 radians) => Sin(NormalizeRadians(radians) + HalfPi);

    private static Fix64 NormalizeRadians(Fix64 value)
    {
        value %= TwoPi;
        if (value > Pi)
        {
            value -= TwoPi;
        }
        else if (value < -Pi)
        {
            value += TwoPi;
        }

        return value;
    }

    public int CompareTo(Fix64 other) => RawValue.CompareTo(other.RawValue);
    public bool Equals(Fix64 other) => RawValue == other.RawValue;
    public override bool Equals(object? obj) => obj is Fix64 other && Equals(other);
    public override int GetHashCode() => RawValue.GetHashCode();
    public override string ToString()
    {
        var negative = RawValue < 0;
        var magnitude = negative ? unchecked(0UL - (ulong)RawValue) : (ulong)RawValue;
        var integer = magnitude >> FractionalBits;
        var fractionalRaw = magnitude & (OneRaw - 1);
        Span<char> text = stackalloc char[44]; // sign + 10 integer + dot + 32 fractional
        var length = 0;
        if (negative) text[length++] = '-';
        integer.TryFormat(text[length..], out var written, provider: CultureInfo.InvariantCulture);
        length += written;
        if (fractionalRaw != 0)
        {
            text[length++] = '.';
            do
            {
                fractionalRaw *= 10;
                text[length++] = (char)('0' + (fractionalRaw >> FractionalBits));
                fractionalRaw &= OneRaw - 1;
            } while (fractionalRaw != 0);
        }
        return new string(text[..length]);
    }

    public string ToString(string? format, IFormatProvider? formatProvider) =>
        string.IsNullOrEmpty(format) || string.Equals(format, "G", StringComparison.OrdinalIgnoreCase)
            ? ToString()
            : ((decimal)this).ToString(format, formatProvider ?? CultureInfo.InvariantCulture);
}
