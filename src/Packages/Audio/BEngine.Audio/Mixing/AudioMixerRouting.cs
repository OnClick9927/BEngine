namespace BEngine.Audio;

internal static class AudioMixerRouting
{
    internal static void Validate(IEnumerable<AudioMixerGroup> groups)
    {
        var visited = new HashSet<AudioMixerGroup>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<AudioMixerGroup>(ReferenceEqualityComparer.Instance);
        foreach (var group in groups) Visit(group, visited, active);
    }
    internal sealed class ValidationBatch
    {
        private readonly HashSet<AudioMixerGroup> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<AudioMixerGroup> _active = new(ReferenceEqualityComparer.Instance);
        internal void Validate(IReadOnlyList<AudioMixerGroup> groups)
        {
            try { for (var i = 0; i < groups.Count; i++) Visit(groups[i], _visited, _active); }
            finally { _visited.Clear(); _active.Clear(); }
        }
    }
    private static void Visit(AudioMixerGroup group, HashSet<AudioMixerGroup> visited, HashSet<AudioMixerGroup> active)
    {
        if (active.Contains(group)) throw new InvalidOperationException("Audio mixer parent/send routes cannot form a cycle.");
        if (!visited.Add(group)) return;
        if (group.isDestroyed || visited.Count > 256) throw new InvalidDataException("Audio mixer routes require live groups and at most 256 buses.");
        if (group.effects is null || group.effects.Count > 16 || group.sends is null || group.sends.Count > 16)
            throw new InvalidDataException("A mixer bus supports at most 16 effects and 16 sends.");
        active.Add(group);
        foreach (var effect in group.effects) { if (effect is null) throw new InvalidDataException("Null audio effect."); effect.Validate(); }
        if (group.parent is { } parent) Visit(parent, visited, active);
        foreach (var send in group.sends)
        {
            if (send is null || send.target is null || send.volume < 0 || send.volume > 1) throw new InvalidDataException("Audio sends require a target and volume in [0, 1].");
            Visit(send.target, visited, active);
        }
        active.Remove(group);
    }
}
