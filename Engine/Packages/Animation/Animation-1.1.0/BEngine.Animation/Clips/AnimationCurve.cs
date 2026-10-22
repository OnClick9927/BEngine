namespace BEngine;

public sealed class AnimationCurve
{
    private readonly List<Keyframe> _keys = [];
    private int _cachedSegment;
    public Keyframe[] keys { get => [.. _keys]; set { var incoming = value ?? []; foreach (var key in incoming) Validate(key); _keys.Clear(); _keys.AddRange(incoming); Sort(); } }
    public int length => _keys.Count;
    internal Fix64 EndTime => _keys.Count == 0 ? Fix64.Zero : _keys[^1].time;
    public WrapMode preWrapMode { get; set; } = WrapMode.ClampForever;
    public WrapMode postWrapMode { get; set; } = WrapMode.ClampForever;
    public AnimationCurve() { }
    public AnimationCurve(params Keyframe[] keys) { this.keys = keys; }
    public int AddKey(Keyframe key) { Validate(key); _keys.RemoveAll(item => item.time == key.time); _keys.Add(key); Sort(); return _keys.FindIndex(item => item.time == key.time); }
    public int AddKey(Fix64 time, Fix64 value) => AddKey(new Keyframe(time, value));
    public int MoveKey(int index, Keyframe key) { Validate(key); _keys.RemoveAt(index); return AddKey(key); }
    public void RemoveKey(int index) { _keys.RemoveAt(index); Sort(); }

    public Fix64 EvaluateDiscrete(Fix64 time)
    {
        if (_keys.Count == 0) return Fix64.Zero;
        time = WrapTime(time, _keys[0].time, _keys[^1].time);
        var low = 0; var high = _keys.Count - 1;
        while (low < high) { var middle = (low + high + 1) / 2; if (_keys[middle].time <= time) low = middle; else high = middle - 1; }
        return _keys[low].value;
    }

    public Fix64 Evaluate(Fix64 time)
    {
        if (_keys.Count == 0) return Fix64.Zero;
        if (_keys.Count == 1) return _keys[0].value;
        time = WrapTime(time, _keys[0].time, _keys[^1].time);
        if (time <= _keys[0].time) return _keys[0].value;
        if (time >= _keys[^1].time) return _keys[^1].value;
        var index = FindSegment(time);
        {
            var left = _keys[index];
            var right = _keys[index + 1];
            if (time == right.time) return right.value;
            if (left.outMode == AnimationTangentMode.Constant || right.inMode == AnimationTangentMode.Constant) return left.value;
            var duration = right.time - left.time;
            var t = (time - left.time) / duration;
            if ((left.weightedMode & WeightedMode.Out) != 0 || (right.weightedMode & WeightedMode.In) != 0)
            {
                var outputWeight = (left.weightedMode & WeightedMode.Out) != 0 ? left.outWeight : Fix64.One / 3;
                var inputWeight = (right.weightedMode & WeightedMode.In) != 0 ? right.inWeight : Fix64.One / 3;
                // Solve the time coordinate of the Bezier, rather than treating time as its parameter.
                var low = Fix64.Zero; var high = Fix64.One;
                for (var iteration = 0; iteration < 32; iteration++)
                {
                    var mid = (low + high) / 2;
                    if (Bezier(0, outputWeight, 1 - inputWeight, 1, mid) < t) low = mid; else high = mid;
                }
                return Bezier(left.value, left.value + duration * outputWeight * left.outTangent,
                    right.value - duration * inputWeight * right.inTangent, right.value, (low + high) / 2);
            }
            var t2 = t * t;
            var t3 = t2 * t;
            var h00 = 2 * t3 - 3 * t2 + 1;
            var h10 = t3 - 2 * t2 + t;
            var h01 = -2 * t3 + 3 * t2;
            var h11 = t3 - t2;
            return h00 * left.value + h10 * duration * left.outTangent +
                   h01 * right.value + h11 * duration * right.inTangent;
        }
    }

    public static AnimationCurve Linear(Fix64 timeStart, Fix64 valueStart, Fix64 timeEnd, Fix64 valueEnd)
    {
        var tangent = timeEnd == timeStart ? Fix64.Zero : (valueEnd - valueStart) / (timeEnd - timeStart);
        return new AnimationCurve(new Keyframe(timeStart, valueStart, tangent, tangent),
            new Keyframe(timeEnd, valueEnd, tangent, tangent));
    }

    private Fix64 WrapTime(Fix64 time, Fix64 start, Fix64 end)
    {
        var duration = end - start;
        if (duration <= Fix64.Zero) return start;
        var mode = time < start ? preWrapMode : postWrapMode;
        if (time >= start && time <= end) return time;
        if (mode == WrapMode.Loop)
        {
            var wrapped = (time - start) % duration;
            if (wrapped < 0) wrapped += duration;
            return start + wrapped;
        }
        if (mode == WrapMode.PingPong)
        {
            var wrapped = (time - start) % (duration * 2);
            if (wrapped < 0) wrapped += duration * 2;
            return wrapped <= duration ? start + wrapped : end - (wrapped - duration);
        }
        return time < start ? start : end;
    }
    private static Fix64 Bezier(Fix64 a, Fix64 b, Fix64 c, Fix64 d, Fix64 t)
    {
        var u = 1 - t;
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
    }

    private void Sort()
    {
        _cachedSegment = 0;
        // Last authored key wins at a duplicate time; no zero-duration segments remain.
        var unique = _keys.GroupBy(key => key.time).Select(group => group.Last()).OrderBy(key => key.time).ToArray();
        _keys.Clear(); _keys.AddRange(unique);
        for (var index = 0; index < _keys.Count; index++)
        {
            var key = _keys[index];
            _keys[index] = key with
            {
                inTangent = Tangent(index, key.inMode, true), outTangent = Tangent(index, key.outMode, false),
                inWeight = Fix64.Clamp(key.inWeight, 0, 1), outWeight = Fix64.Clamp(key.outWeight, 0, 1)
            };
        }
    }

    private int FindSegment(Fix64 time)
    {
        if (_cachedSegment < _keys.Count - 1 && time > _keys[_cachedSegment].time && time <= _keys[_cachedSegment + 1].time)
            return _cachedSegment;
        var low = 1; var high = _keys.Count - 1;
        while (low < high) { var middle = (low + high) / 2; if (_keys[middle].time < time) low = middle + 1; else high = middle; }
        return _cachedSegment = low - 1;
    }

    private static void Validate(Keyframe key)
    {
        if (!Enum.IsDefined(key.inMode) || !Enum.IsDefined(key.outMode) || (key.weightedMode & ~WeightedMode.Both) != 0)
            throw new ArgumentException("Invalid animation tangent mode.");
    }

    private Fix64 Tangent(int index, AnimationTangentMode mode, bool input)
    {
        var key = _keys[index];
        if (mode == AnimationTangentMode.Free) return input ? key.inTangent : key.outTangent;
        if (mode == AnimationTangentMode.Constant || _keys.Count < 2) return 0;
        var before = _keys[Math.Max(0, index - 1)];
        var after = _keys[Math.Min(_keys.Count - 1, index + 1)];
        var left = index == 0 ? (after.value - key.value) / (after.time - key.time) : (key.value - before.value) / (key.time - before.time);
        var right = index == _keys.Count - 1 ? left : (after.value - key.value) / (after.time - key.time);
        if (mode == AnimationTangentMode.Linear) return input ? left : right;
        var slope = (after.value - before.value) / (after.time - before.time);
        if (mode == AnimationTangentMode.ClampedAuto)
        {
            if (left == 0 || right == 0 || (left > 0) != (right > 0)) return 0;
            var limit = 3 * Fix64.Min(Fix64.Abs(left), Fix64.Abs(right));
            slope = Fix64.Clamp(slope, -limit, limit);
        }
        return slope;
    }
}
