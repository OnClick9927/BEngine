namespace BEngine.PostProcessing;

[AddComponentMenu("Rendering/Post-process Volume")]
public sealed class PostProcessVolume : Behaviour
{
    public bool isGlobal { get; set; } = true;
    public Fix64 priority { get; set; }
    public Fix64 weight
    {
        get;
        set => field = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
    } = Fix64.One;
    public Fix64 blendDistance
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    }
    public Fix64 radius
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = 5;
    public Vector2 center { get; set; }
    public PostProcessProfile? profile { get; set; }

    internal Fix64 GetInfluence(Vector2 triggerPosition)
    {
        if (!enabled || !gameObject.activeInHierarchy || profile is null || weight <= Fix64.Zero)
            return Fix64.Zero;
        if (isGlobal) return weight;
        var distance = Vector2.Distance(transform.TransformPoint(center), triggerPosition);
        if (distance <= radius) return weight;
        if (blendDistance <= Fix64.Zero || distance >= radius + blendDistance) return Fix64.Zero;
        return weight * (Fix64.One - (distance - radius) / blendDistance);
    }
}
