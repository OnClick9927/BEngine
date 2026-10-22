namespace BEngine.TiledMap;

public sealed partial class Tilemap
{
    /// <summary>Applies cells in 128-cell steps; the final serialized string is committed atomically.</summary>
    /// <remarks>The input list must remain unchanged until completion. Cancellation rolls the batch back.</remarks>
    public BTask SetTilesAsync(IReadOnlyList<TileCell> cells, BCancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count > 1_000_000 || _editDepth != 0) throw new InvalidOperationException("Async tile editing requires an idle map and at most one million input cells.");
        var completion = new BTaskCompletionSource(); var edit = BeginEdit(); var index = 0; var finished = false;
        IDisposable? work = null;
        void Fail(Exception error)
        {
            if (finished) return; finished = true;
            try { edit.Cancel(); edit.Dispose(); }
            catch (Exception rollbackError) { error = new AggregateException(error, rollbackError); }
            finally { work?.Dispose(); completion.TrySetException(error); }
        }
        try
        {
            work = CooperativeWorkScheduler.Main.Schedule(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isDestroyed || _editDepth == 0) throw new InvalidOperationException("The map was destroyed or deserialized during async editing.");
                var end = Math.Min(cells.Count, index + 128);
                for (; index < end; index++) { var cell = cells[index]; SetTile(cell.Position, cell.TileId, cell.Transform); }
                progress?.Report(cells.Count == 0 ? 1 : (double)index / cells.Count);
                if (index < cells.Count) return CooperativeWorkStatus.Continue;
                edit.Dispose(); finished = true; completion.TrySetResult(); return CooperativeWorkStatus.Complete;
            }, Fail, CooperativeWorkPriority.Background);
        }
        catch (Exception error) { Fail(error); }
        return completion.Task;
    }
}
