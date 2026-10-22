namespace BEngine.Physics2D;

public struct ContactFilter2D
{
    public bool useTriggers { get; set; }
    public bool useLayerMask { get; set; }
    public LayerMask layerMask { get; set; }

    public static ContactFilter2D NoFilter() => new()
    {
        useTriggers = true,
        useLayerMask = false,
        layerMask = new LayerMask(ulong.MaxValue)
    };

    public ContactFilter2D SetLayerMask(LayerMask mask)
    {
        layerMask = mask;
        useLayerMask = true;
        return this;
    }

    public ContactFilter2D ClearLayerMask()
    {
        useLayerMask = false;
        return this;
    }

    public readonly bool IsFilteringTrigger(Collider2D collider) => !useTriggers && collider.isTrigger;
    public readonly bool IsFilteringLayerMask(GameObject gameObject) =>
        useLayerMask && !layerMask.Contains(gameObject.layer);

    internal readonly bool Allows(Collider2D collider) =>
        !IsFilteringTrigger(collider) && !IsFilteringLayerMask(collider.gameObject);
}
