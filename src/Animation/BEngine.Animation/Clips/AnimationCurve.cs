namespace BEngine.Animation;

public sealed class AnimationCurve
{
    private readonly List<Keyframe> _keys = [];
    public Keyframe[] keys { get => [.. _keys]; set { _keys.Clear(); _keys.AddRange(value ?? []); Sort(); } }
    public int length => _keys.Count;
    public WrapMode preWrapMode { get; set; } = WrapMode.ClampForever;
    public WrapMode postWrapMode { get; set; } = WrapMode.ClampForever;
    public AnimationCurve(params Keyframe[] keys) { this.keys = keys; }
    public int AddKey(Keyframe key) { _keys.Add(key); Sort(); return _keys.IndexOf(key); }
    public int AddKey(Fix64 time, Fix64 value) => AddKey(new Keyframe(time, value));
    public int MoveKey(int index, Keyframe key) { _keys[index] = key; Sort(); return _keys.IndexOf(key); }
    public void RemoveKey(int index) => _keys.RemoveAt(index);

    public Fix64 Evaluate(Fix64 time)
    {
        if (_keys.Count == 0) return Fix64.Zero;
        if (_keys.Count == 1) return _keys[0].value;
        time = WrapTime(time, _keys[0].time, _keys[^1].time);
        if (time <= _keys[0].time) return _keys[0].value;
        if (time >= _keys[^1].time) return _keys[^1].value;
        for (var index = 0; index < _keys.Count - 1; index++)
        {
            var left = _keys[index];
            var right = _keys[index + 1];
            if (time > right.time) continue;
            var duration = right.time - left.time;
            var t = (time - left.time) / duration;
            var t2 = t * t;
            var t3 = t2 * t;
            var h00 = 2 * t3 - 3 * t2 + 1;
            var h10 = t3 - 2 * t2 + t;
            var h01 = -2 * t3 + 3 * t2;
            var h11 = t3 - t2;
            return h00 * left.value + h10 * duration * left.outTangent +
                   h01 * right.value + h11 * duration * right.inTangent;
        }
        return _keys[^1].value;
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
    private void Sort() => _keys.Sort((left, right) => left.time.CompareTo(right.time));
}
