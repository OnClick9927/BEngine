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
    public static Fix64 FromDecimal(decimal value) => FromRaw(decimal.ToInt64(value * OneRaw));

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
        var raw = BigInteger.Parse(significantDigits, NumberStyles.None, CultureInfo.InvariantCulture) * OneRaw;
        if (scale > 0)
        {
            if (scale > significantDigits.Length + 10L)
            {
                return Zero;
            }

            raw /= BigInteger.Pow(10, checked((int)scale));
        }
        else if (scale < 0)
        {
            if (-scale > 10)
            {
                throw new OverflowException($"'{original.ToString()}' is outside the Fix64 range.");
            }

            raw *= BigInteger.Pow(10, checked((int)-scale));
        }

        if (negative)
        {
            raw = -raw;
        }

        if (raw < long.MinValue || raw > long.MaxValue)
        {
            throw new OverflowException($"'{original.ToString()}' is outside the Fix64 range.");
        }

        return FromRaw((long)raw);
    }

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
    public static implicit operator Fix64(long value) => FromRaw(checked(value << FractionalBits));
    public static explicit operator Fix64(float value) => FromDecimal((decimal)value);
    public static explicit operator Fix64(double value) => FromDecimal((decimal)value);
    public static explicit operator int(Fix64 value) => (int)(value.RawValue >> FractionalBits);
    public static explicit operator long(Fix64 value) => value.RawValue >> FractionalBits;
    public static explicit operator float(Fix64 value) => (float)((double)value.RawValue / OneRaw);
    public static explicit operator double(Fix64 value) => (double)value.RawValue / OneRaw;
    public static explicit operator decimal(Fix64 value) => (decimal)value.RawValue / OneRaw;

    public static Fix64 operator +(Fix64 left, Fix64 right) => FromRaw(checked(left.RawValue + right.RawValue));
    public static Fix64 operator -(Fix64 left, Fix64 right) => FromRaw(checked(left.RawValue - right.RawValue));
    public static Fix64 operator -(Fix64 value) => FromRaw(checked(-value.RawValue));
    public static Fix64 operator *(Fix64 left, Fix64 right) =>
        FromRaw(checked((long)(((Int128)left.RawValue * right.RawValue) >> FractionalBits)));
    public static Fix64 operator /(Fix64 left, Fix64 right)
    {
        if (right.RawValue == 0)
        {
            throw new DivideByZeroException();
        }

        return FromRaw(checked((long)(((Int128)left.RawValue << FractionalBits) / right.RawValue)));
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

        var scaled = (Int128)value.RawValue << FractionalBits;
        var estimate = scaled;
        var next = (estimate + 1) >> 1;
        while (next < estimate)
        {
            estimate = next;
            next = (estimate + (scaled / estimate)) >> 1;
        }

        return FromRaw((long)estimate);
    }

    public static Fix64 Sin(Fix64 radians)
    {
        var x = NormalizeRadians(radians);
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
        return result + term;
    }

    public static Fix64 Cos(Fix64 radians) => Sin(radians + HalfPi);

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
        var raw = new BigInteger(RawValue);
        var negative = raw.Sign < 0;
        var magnitude = BigInteger.Abs(raw);
        var integer = magnitude >> FractionalBits;
        var fractionalRaw = magnitude & (OneRaw - 1);
        var sign = negative ? "-" : string.Empty;
        if (fractionalRaw.IsZero)
        {
            return sign + integer.ToString(CultureInfo.InvariantCulture);
        }

        var fractional = (fractionalRaw * BigInteger.Pow(5, FractionalBits))
            .ToString("D32", CultureInfo.InvariantCulture)
            .TrimEnd('0');
        return $"{sign}{integer.ToString(CultureInfo.InvariantCulture)}.{fractional}";
    }

    public string ToString(string? format, IFormatProvider? formatProvider) =>
        string.IsNullOrEmpty(format) || string.Equals(format, "G", StringComparison.OrdinalIgnoreCase)
            ? ToString()
            : ((decimal)this).ToString(format, formatProvider ?? CultureInfo.InvariantCulture);
}
