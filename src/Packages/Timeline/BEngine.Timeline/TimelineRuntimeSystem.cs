namespace BEngine;

public sealed class TimelineRuntimeSystem : ISceneRuntimeSystem
{
    private readonly List<PlayableDirector> _directors = [];
    private readonly HashSet<PlayableDirector> _tracked = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PlayableDirector> _present = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PlayableDirector> _controlledThisFrame = [];
    private bool _controlledInUse;
    public int order => 55;
    public string packageId => "com.bengine.timeline";

    public void Update(Scene scene, Fix64 deltaTime)
    {
        scene.GetComponents(_directors);
        try { OrderControlParentsFirst(); }
        catch
        {
            StopDirectors(_directors);
            throw;
        }
        _present.Clear();
        // Capture controlled membership at the same point as the old local
        // set, then let Tick/CollectControlled add newly acquired children.
        // A nested update must not clear the outer call's captured membership.
        var sharedControls = !_controlledInUse;
        var controlledThisFrame = sharedControls ? _controlledThisFrame : new HashSet<PlayableDirector>();
        if (sharedControls) _controlledInUse = true;
        try
        {
            controlledThisFrame.Clear();
            foreach (var director in _directors)
                if (director.isControlled) controlledThisFrame.Add(director);
            foreach (var director in _directors)
            {
                if (director.isDestroyed) continue;
                _present.Add(director);
                if (controlledThisFrame.Contains(director)) continue;
                if (director.enabled && director.gameObject.activeInHierarchy) director.Tick(deltaTime);
                else director.Suspend();
                director.CollectControlled(controlledThisFrame);
            }
            foreach (var director in _tracked)
                if (!_present.Contains(director)) director.Suspend();
            _tracked.Clear();
            // HashSet.UnionWith enumerates its IEnumerable argument through a
            // boxed enumerator, even when both sets are already reusable.
            foreach (var director in _present) _tracked.Add(director);
        }
        finally
        {
            // Do not retain a removed/failed director until another update.
            controlledThisFrame.Clear();
            if (sharedControls) _controlledInUse = false;
        }
    }

    private void OrderControlParentsFirst()
    {
        // Without a live Control clip there are no graph edges, so the current
        // scene order is already the topological order. Inspect mutable tracks
        // on each update: adding or unmuting a Control track must take effect
        // immediately, including nested assets reached through that track.
        var hasControls = false;
        foreach (var director in _directors)
        {
            if (director.playableAsset is not { isDestroyed: false } asset) continue;
            foreach (var track in asset.tracks)
                if (!track.muted && track.type == TimelineTrackType.Control && track.clips.Count > 0)
                { hasControls = true; break; }
            if (hasControls) break;
        }
        if (!hasControls) return;
        OrderControlGraph();
    }

    private void OrderControlGraph()
    {
        // Keep the graph's captured incoming lookup outside the no-Control
        // method; otherwise the compiler allocates its closure before the
        // early return even though no graph is needed.
        var present = _directors.ToHashSet();
        var incoming = _directors.ToDictionary(item => item, _ => 0);
        var edges = _directors.ToDictionary(item => item, _ => new HashSet<PlayableDirector>());
        var pending = new Queue<(PlayableDirector Director, TimelineAsset? Asset)>(_directors.Select(item => (item, item.playableAsset)));
        var visited = new HashSet<(PlayableDirector, TimelineAsset?)>();
        while (pending.TryDequeue(out var input))
        {
            if (!visited.Add(input)) continue;
            foreach (var child in input.Director.ControlInputs(input.Asset))
            {
                if (!present.Contains(child.Director)) continue;
                edges[input.Director].Add(child.Director);
                pending.Enqueue(child);
            }
        }
        foreach (var children in edges.Values)
            foreach (var child in children) incoming[child]++;
        var ready = new Queue<PlayableDirector>(_directors.Where(item => incoming[item] == 0));
        var ordered = new List<PlayableDirector>(_directors.Count);
        while (ready.TryDequeue(out var parent))
        {
            ordered.Add(parent);
            foreach (var child in edges[parent])
                if (--incoming[child] == 0) ready.Enqueue(child);
        }
        if (ordered.Count != _directors.Count)
        {
            throw new InvalidOperationException("Cyclic Timeline control bindings in the scene.");
        }
        _directors.Clear(); _directors.AddRange(ordered);
    }

    public void Stop(Scene scene)
    {
        scene.GetComponents(_directors);
        StopDirectors(_directors.Concat(_tracked));
        _tracked.Clear();
        _present.Clear();
        _directors.Clear();
    }

    private static void StopDirectors(IEnumerable<PlayableDirector> source)
    {
        var directors = source.Where(item => !item.isDestroyed).Distinct().ToArray();
        // Releasing a parent restores its children's previous playback. Release every root
        // first, then stop all directors so scene shutdown cannot revive a child.
        foreach (var director in directors.Where(item => !item.isControlled).ToArray()) director.Stop();
        foreach (var director in directors) director.Stop();
    }
}
