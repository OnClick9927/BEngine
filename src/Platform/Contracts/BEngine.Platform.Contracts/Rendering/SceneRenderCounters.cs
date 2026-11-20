namespace BEngine.Rendering;

/// <summary>Immutable descriptor shared by renderer-owned numeric counters.</summary>
public sealed class SceneRenderCounterLayout
{
    private readonly string[] _names;
    public SceneRenderCounterLayout(IReadOnlyList<string> names, IReadOnlyList<string>? floatingPointNames = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count > 16 || names.Any(string.IsNullOrWhiteSpace) || ((((global::System.Collections.Generic.IEnumerable<string>)(names)), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems445, distinctComparer445) => distinctItems445.ToHashSet<string>(distinctComparer445).Count
        }) != names.Count)
            throw new ArgumentException("A counter layout supports up to sixteen distinct names.", nameof(names));
        _names = names.ToArray();
        Names = Array.AsReadOnly(_names);
        FloatingPointNames = Array.AsReadOnly((floatingPointNames ?? []).ToArray());
        if (floatingPointNames is not null)
            foreach (var name in floatingPointNames)
            {
                var index = Array.IndexOf(_names, name);
                if (index < 0) throw new ArgumentException("Floating point counter is not in the layout.", nameof(floatingPointNames));
                FloatingPointMask |= 1 << index;
            }
    }
    internal int FloatingPointMask { get; }
    internal bool IsFloatingPoint(int index) => (FloatingPointMask & (1 << index)) != 0;
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<string> FloatingPointNames { get; }
    internal ReadOnlySpan<string> NameSpan => _names;
}

/// <summary>Inline snapshot of optional renderer counters; no per-frame collection allocation.</summary>
public struct SceneRenderCounters
{
    // Explicit fields keep the snapshot layout predictable across CoreCLR and Mono/AOT.
    private long _value0, _value1, _value2, _value3, _value4, _value5, _value6, _value7,
        _value8, _value9, _value10, _value11, _value12, _value13, _value14, _value15;
    public SceneRenderCounterLayout? Layout { get; init; }
    public long Value0 { readonly get => _value0; init => _value0 = value; }
    public long Value1 { readonly get => _value1; init => _value1 = value; }
    public long Value2 { readonly get => _value2; init => _value2 = value; }
    public long Value3 { readonly get => _value3; init => _value3 = value; }
    public long Value4 { readonly get => _value4; init => _value4 = value; }
    public long Value5 { readonly get => _value5; init => _value5 = value; }
    public long Value6 { readonly get => _value6; init => _value6 = value; }
    public long Value7 { readonly get => _value7; init => _value7 = value; }
    public long Value8 { readonly get => _value8; init => _value8 = value; }
    public long Value9 { readonly get => _value9; init => _value9 = value; }
    public long Value10 { readonly get => _value10; init => _value10 = value; }
    public long Value11 { readonly get => _value11; init => _value11 = value; }
    public long Value12 { readonly get => _value12; init => _value12 = value; }
    public long Value13 { readonly get => _value13; init => _value13 = value; }
    public long Value14 { readonly get => _value14; init => _value14 = value; }
    public long Value15 { readonly get => _value15; init => _value15 = value; }
    public SceneRenderCounters(SceneRenderCounterLayout layout, ReadOnlySpan<long> values)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Names.Count != values.Length) throw new ArgumentException("Counter count does not match its layout.", nameof(values));
        this = default;
        Layout = layout;
        for (var index = 0; index < values.Length; index++) AddValue(index, values[index]);
    }
    public readonly long GetValue(string name)
    {
        if (Layout is null) return 0;
        var index = Layout.NameSpan.IndexOf(name);
        return index < 0 ? 0 : GetValue(index);
    }
    public readonly double GetDoubleValue(string name)
    {
        if (Layout is null) return 0;
        var index = Layout.NameSpan.IndexOf(name);
        return index < 0 ? 0 : Layout.IsFloatingPoint(index) ? BitConverter.Int64BitsToDouble(GetValue(index)) : GetValue(index);
    }
    private readonly long GetValue(int index) => index switch
    { 0 => _value0, 1 => _value1, 2 => _value2, 3 => _value3, 4 => _value4, 5 => _value5, 6 => _value6, 7 => _value7, 8 => _value8, 9 => _value9, 10 => _value10, 11 => _value11,
        12 => _value12, 13 => _value13, 14 => _value14, 15 => _value15, _ => 0 };
    private void AddValue(int index, long value)
    {
        if (Layout!.IsFloatingPoint(index))
            value = BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(GetValue(index)) + BitConverter.Int64BitsToDouble(value));
        switch (index)
        {
            case 0: _value0 = Layout.IsFloatingPoint(index) ? value : _value0 + value; break; case 1: _value1 = Layout.IsFloatingPoint(index) ? value : _value1 + value; break;
            case 2: _value2 = Layout.IsFloatingPoint(index) ? value : _value2 + value; break; case 3: _value3 = Layout.IsFloatingPoint(index) ? value : _value3 + value; break;
            case 4: _value4 = Layout.IsFloatingPoint(index) ? value : _value4 + value; break; case 5: _value5 = Layout.IsFloatingPoint(index) ? value : _value5 + value; break;
            case 8: _value8 = Layout.IsFloatingPoint(index) ? value : _value8 + value; break; case 9: _value9 = Layout.IsFloatingPoint(index) ? value : _value9 + value; break;
            case 10: _value10 = Layout.IsFloatingPoint(index) ? value : _value10 + value; break; case 11: _value11 = Layout.IsFloatingPoint(index) ? value : _value11 + value; break;
            case 12: _value12 = Layout.IsFloatingPoint(index) ? value : _value12 + value; break; case 13: _value13 = Layout.IsFloatingPoint(index) ? value : _value13 + value; break;
            case 14: _value14 = Layout.IsFloatingPoint(index) ? value : _value14 + value; break; case 15: _value15 = Layout.IsFloatingPoint(index) ? value : _value15 + value; break;
            case 6: _value6 = Layout.IsFloatingPoint(index) ? value : _value6 + value; break; case 7: _value7 = Layout.IsFloatingPoint(index) ? value : _value7 + value; break;
        }
    }
    public static SceneRenderCounters Add(SceneRenderCounters left, SceneRenderCounters right)
    {
        if (left.Layout is null) return right;
        if (right.Layout is null) return left;
        if (ReferenceEquals(left.Layout, right.Layout))
            for (var index = 0; index < left.Layout.Names.Count; index++) left.AddValue(index, right.GetValue(index));
        else
            // A mixed-renderer capture keeps the first layout and sums shared counter names.
            for (var index = 0; index < left.Layout.Names.Count; index++)
            {
                var other = right.Layout.NameSpan.IndexOf(left.Layout.Names[index]);
                // A reused name with a different representation is a different counter schema.
                if (other < 0 || left.Layout.IsFloatingPoint(index) != right.Layout.IsFloatingPoint(other)) continue;
                left.AddValue(index, right.GetValue(other));
            }
        return left;
    }
}
