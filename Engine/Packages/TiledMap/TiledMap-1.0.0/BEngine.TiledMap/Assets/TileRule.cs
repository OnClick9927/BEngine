namespace BEngine.TiledMap;

public enum TileNeighborMatch { Any, Same, Different, Empty, Occupied, TileId }

public sealed class TileNeighborCondition
{
    public int X { get; set; }
    public int Y { get; set; }
    public TileNeighborMatch Match { get; set; } = TileNeighborMatch.Same;
    public int TileId { get; set; }
}

/// <summary>Rules are tested in authored order against the original cell IDs.</summary>
public sealed class TileRule
{
    public List<TileNeighborCondition> Neighbors { get; set; } = [];
    public int OutputTileId { get; set; }
}

public sealed class TileAnimationFrame
{
    public int TileId { get; set; }
    public float Duration { get; set; } = 0.1f;
}

/// <summary>Resolves visual variants without changing serialized cells or collision geometry.</summary>
public static class TileVisualResolver
{
    public static TileDefinition? Resolve(Tilemap map, TileCoordinate position,
        IReadOnlyDictionary<int, TileDefinition> definitions, Fix64 elapsedTime)
    {
        if (!definitions.TryGetValue(map.GetTile(position), out var source)) return null;
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(source.Rules.Count, source.Animation.Count) + 1);
        var visual = source.Rules.Count == 0 ? source : ResolveRules(map, position, definitions, source);
        // Outputs are terminal: a rule cannot create a recursive rule cycle.
        if (visual.Animation.Count == 0) return visual;
        using var frameBudget = LoopExecutionGuard.AllowIterations(visual.Animation.Count + 1);
        var total = Fix64.Zero;
        foreach (var frame in visual.Animation)
        {
            if (!float.IsFinite(frame.Duration) || frame.Duration <= 0) return visual;
            total += (Fix64)(double)frame.Duration;
        }
        if (total <= Fix64.Zero) return visual;
        var time = Fix64.Max(Fix64.Zero, elapsedTime) % total;
        foreach (var frame in visual.Animation)
        {
            var duration = (Fix64)(double)frame.Duration;
            if (time < duration) return definitions.GetValueOrDefault(frame.TileId) ?? visual;
            time -= duration;
        }
        return visual;
    }

    private static TileDefinition ResolveRules(Tilemap map, TileCoordinate position,
        IReadOnlyDictionary<int, TileDefinition> definitions, TileDefinition source)
    {
        var visual = source;
        foreach (var rule in source.Rules)
        {
            if (!rule.Neighbors.All(condition => Matches(map, position, source.Id, condition))) continue;
            if (definitions.TryGetValue(rule.OutputTileId, out var output)) visual = output;
            break;
        }
        return visual;
    }

    private static bool Matches(Tilemap map, TileCoordinate origin, int sourceId, TileNeighborCondition condition)
    {
        var x = (long)origin.X + condition.X; var y = (long)origin.Y + condition.Y;
        var id = x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue
            ? 0 : map.GetTile(new TileCoordinate((int)x, (int)y));
        return condition.Match switch
        {
            TileNeighborMatch.Any => true,
            TileNeighborMatch.Same => id == sourceId,
            TileNeighborMatch.Different => id != sourceId,
            TileNeighborMatch.Empty => id == 0,
            TileNeighborMatch.Occupied => id != 0,
            TileNeighborMatch.TileId => id == condition.TileId,
            _ => false
        };
    }
}
