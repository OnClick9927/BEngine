namespace BEngine;

public sealed partial class PlayableDirector
{
    private void DispatchSignalRange(TimelineAsset asset, Fix64 from, Fix64 to, Fix64 length, bool loop,
        HashSet<PlayableDirector> stack)
    {
        if (length <= 0 || from == to) return;
        if (!stack.Add(this) || stack.Count > 64) throw new InvalidOperationException("Cyclic Timeline signal control binding.");
        try
        {
            DispatchSignals(asset, from, to, length, loop);
            var reverse = to < from;
            var first = loop ? Mathf.FloorToInt(Fix64.Min(from, to) / length) : 0;
            var last = loop ? Mathf.FloorToInt(Fix64.Max(from, to) / length) : 0;
            if ((long)last - first > 10000) throw new InvalidOperationException("Timeline signal step crosses too many cycles.");
            for (long step = 0; step <= (long)last - first; step++)
            {
                var cycle = reverse ? last - step : first + step;
                var offset = (Fix64)cycle * length;
                var low = Fix64.Clamp(Fix64.Min(from, to) - offset, 0, length);
                var high = Fix64.Clamp(Fix64.Max(from, to) - offset, 0, length);
                if (low == high) continue;
                foreach (var track in asset.tracks.Where(item => !item.muted && item.type == TimelineTrackType.Control))
                {
                    if (GetGenericBinding(track.id)?.GetComponent<PlayableDirector>() is not { isDestroyed: false } child) continue;
                    var boundaries = track.clips.SelectMany(clip => new[] { clip.start, clip.end })
                        .Where(time => time > low && time < high).Append(low).Append(high).Distinct().Order().ToArray();
                    for (var part = 0; part < boundaries.Length - 1; part++)
                    {
                        var index = reverse ? boundaries.Length - 2 - part : part;
                        var a = boundaries[index]; var b = boundaries[index + 1]; var sample = (a + b) / 2;
                        var clip = track.clips.Where(item => item.duration > 0 && sample >= item.start && sample < item.end)
                            .OrderBy(item => item.start).LastOrDefault();
                        if (clip is null) continue;
                        var nested = clip.ResolveNestedTimeline(_controlled.TryGetValue(child, out var saved) ? saved.Asset : child.playableAsset);
                        if (nested is null || nested.isDestroyed) continue;
                        var localFrom = ((reverse ? b : a) - clip.start) * clip.timeScale + clip.clipIn;
                        var localTo = ((reverse ? a : b) - clip.start) * clip.timeScale + clip.clipIn;
                        // A marker exactly at an entered boundary belongs to that entry; normal
                        // adjacent updates exclude the previous endpoint to avoid duplicate signals.
                        if (!reverse && a == clip.start && from < offset + a) localFrom -= Fix64.FromRaw(1);
                        if (reverse && b == clip.end && from > offset + b) localFrom += Fix64.FromRaw(1);
                        child.DispatchSignalRange(nested, localFrom, localTo, nested.duration, clip.loopNested, stack);
                    }
                }
            }
        }
        finally { stack.Remove(this); }
    }
}
