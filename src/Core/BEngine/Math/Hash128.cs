namespace BEngine;

public readonly struct Hash128 : IEquatable<Hash128>
{
    private readonly ulong _low;
    private readonly ulong _high;
    public bool isValid => _low != 0 || _high != 0;
    public Hash128(uint u32_0, uint u32_1, uint u32_2, uint u32_3)
    {
        _low = ((ulong)u32_1 << 32) | u32_0;
        _high = ((ulong)u32_3 << 32) | u32_2;
    }
    private Hash128(ulong low, ulong high) { _low = low; _high = high; }
    public static Hash128 Compute(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(data));
        return new Hash128(BitConverter.ToUInt64(bytes, 0), BitConverter.ToUInt64(bytes, 8));
    }
    public static Hash128 Parse(string hash) => TryParse(hash, out var value)
        ? value : throw new FormatException($"'{hash}' is not a Hash128 value.");
    public static bool TryParse(string? hash, out Hash128 value)
    {
        value = default;
        if (hash is null || hash.Length != 32 || !ulong.TryParse(hash[..16],
                System.Globalization.NumberStyles.HexNumber, null, out var high) ||
            !ulong.TryParse(hash[16..], System.Globalization.NumberStyles.HexNumber, null, out var low)) return false;
        value = new Hash128(low, high);
        return true;
    }
    public bool Equals(Hash128 other) => _low == other._low && _high == other._high;
    public override bool Equals(object? obj) => obj is Hash128 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_low, _high);
    public override string ToString() => $"{_high:x16}{_low:x16}";
    public static bool operator ==(Hash128 left, Hash128 right) => left.Equals(right);
    public static bool operator !=(Hash128 left, Hash128 right) => !left.Equals(right);
}
