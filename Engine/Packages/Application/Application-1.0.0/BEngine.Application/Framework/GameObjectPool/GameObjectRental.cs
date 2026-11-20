namespace BEngine.App.Pooling;

/// <summary>A generation-checked rental. Returning it goes through the owning service's operation
/// boundary so a domain replacement requested by a callback can finish releasing safely.</summary>
public sealed class GameObjectRental : IDisposable
{
    private GameObjectPoolService? _service;
    private PooledObject<GameObject>? _rental;
    internal GameObjectRental(GameObjectPoolService service, PooledObject<GameObject> rental)
    { _service = service; _rental = rental; }
    public bool IsReturned => _rental is null || _rental.IsReturned;
    public GameObject Value => _rental?.Value ?? throw new ObjectDisposedException(nameof(GameObjectRental));
    public void Dispose()
    {
        if (_rental is not { } rental) return;
        var service = _service!;
        try { service.ReturnRental(rental); }
        finally
        {
            // A rejected recursive return has not returned the underlying rental. Keep it usable
            // so the owner can retry at a valid operation boundary.
            if (rental.IsReturned) { _rental = null; _service = null; }
        }
    }
}
