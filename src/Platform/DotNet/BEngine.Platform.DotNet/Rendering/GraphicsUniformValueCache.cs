using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using NumericsVector4 = System.Numerics.Vector4;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;

namespace BEngine.Rendering.Rhi;

/// <summary>Bounded, per-program native uniform state. Record only after a successful API call.
/// Locations, rather than names, keep array-name aliases coherent. A device generation invalidates
/// values after external native rendering; bulk writes must clear overlapping cached values.</summary>
internal sealed class GraphicsUniformValueCache
{
    internal const int MaximumEntries = 64;
    private readonly Dictionary<int, Value> _values = [];
    private enum Kind { Matrix, Vector, Float, Int }
    private readonly struct Value(long generation, Kind kind, int scalar,
        NumericsVector4 vector, NumericsMatrix4x4 matrix)
    {
        internal readonly long Generation = generation;
        internal readonly Kind Kind = kind;
        internal readonly int Scalar = scalar;
        internal readonly NumericsVector4 Vector = vector;
        internal readonly NumericsMatrix4x4 Matrix = matrix;
    }
    internal int Count => _values.Count;

    internal bool Matches(int location, long generation, in NumericsMatrix4x4 value)
    {
        ref var cached = ref CollectionsMarshal.GetValueRefOrNullRef(_values, location);
        return Matches(ref cached, generation, Kind.Matrix) && SameBits(in cached.Matrix, in value);
    }
    internal bool Matches(int location, long generation, in NumericsVector4 value)
    {
        ref var cached = ref CollectionsMarshal.GetValueRefOrNullRef(_values, location);
        return Matches(ref cached, generation, Kind.Vector) && SameBits(in cached.Vector, in value);
    }
    internal bool Matches(int location, long generation, float value)
    {
        ref var cached = ref CollectionsMarshal.GetValueRefOrNullRef(_values, location);
        return Matches(ref cached, generation, Kind.Float) && cached.Scalar == BitConverter.SingleToInt32Bits(value);
    }
    internal bool Matches(int location, long generation, int value)
    {
        ref var cached = ref CollectionsMarshal.GetValueRefOrNullRef(_values, location);
        return Matches(ref cached, generation, Kind.Int) && cached.Scalar == value;
    }

    internal void Record(int location, long generation, NumericsMatrix4x4 value) =>
        Store(location, new(generation, Kind.Matrix, 0, default, value));
    internal void Record(int location, long generation, NumericsVector4 value) =>
        Store(location, new(generation, Kind.Vector, 0, value, default));
    internal void Record(int location, long generation, float value) =>
        Store(location, new(generation, Kind.Float, BitConverter.SingleToInt32Bits(value), default, default));
    internal void Record(int location, long generation, int value) =>
        Store(location, new(generation, Kind.Int, value, default, default));

    internal void Clear() => _values.Clear();

    internal static bool SameBits<T>(in T left, in T right) where T : unmanaged =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in left, 1)).SequenceEqual(
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in right, 1)));

    private static bool Matches(ref Value value, long generation, Kind kind) =>
        !Unsafe.IsNullRef(ref value) && value.Generation == generation && value.Kind == kind;

    private void Store(int location, Value value)
    {
        // User shader names and native locations are not a bounded input.
        if (_values.Count == MaximumEntries && !_values.ContainsKey(location)) _values.Clear();
        _values[location] = value;
    }
}
