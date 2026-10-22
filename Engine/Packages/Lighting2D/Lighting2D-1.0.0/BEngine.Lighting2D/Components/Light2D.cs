namespace BEngine;

[AddComponentMenu("Rendering/2D Light")]
public sealed partial class Light2D : MonoBehaviour, ISpriteReferenceOwner
{
    private static readonly Fix64 MinimumRadius = Fix64.Parse("0.001");

    public Light2DType lightType { get; set; } = Light2DType.Point;
    public Color color { get; set; } = Color.white;
    public Fix64 intensity
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.One;
    public Fix64 radius
    {
        get;
        set => field = Fix64.Max(MinimumRadius, value);
    } = 5;
    public Fix64 innerRadius
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    }
    public Fix64 outerAngle
    {
        get;
        set => field = Fix64.Clamp(value, Fix64.Parse("0.1"), 360);
    } = 90;
    public Fix64 innerAngle
    {
        get;
        set => field = Fix64.Clamp(value, Fix64.Zero, 360);
    } = 45;
    public LayerMask targetLayers { get; set; } = new(ulong.MaxValue);
    public bool castShadows { get; set; }
    public LayerMask shadowCasterLayers { get; set; } = new(ulong.MaxValue);
    public Fix64 shadowStrength
    {
        get;
        set => field = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
    } = Fix64.One;

    public Fix64 GetIntensityAt(Vector2 worldPosition)
    {
        if (!enabled || !gameObject.activeInHierarchy || intensity <= Fix64.Zero)
            return Fix64.Zero;
        if (lightType == Light2DType.Global) return intensity;
        if (lightType == Light2DType.Freeform) return intensity * ShapeAttenuation(worldPosition);
        if (lightType == Light2DType.Sprite) return intensity * SampleCookie(worldPosition).a;

        var toPoint = worldPosition - transform.position;
        var distance = toPoint.magnitude;
        var outerRadius = radius;
        if (distance >= outerRadius) return Fix64.Zero;
        var innerRadiusValue = Fix64.Min(innerRadius, outerRadius);
        var radial = distance <= innerRadiusValue
            ? Fix64.One
            : Fix64.One - Mathf.InverseLerp(innerRadiusValue, outerRadius, distance);
        radial *= radial;
        if (lightType != Light2DType.Spot || distance <= Fix64.Epsilon)
            return intensity * radial;

        var direction = Transform.RotateVector(Vector2.right, transform.rotation);
        var alignment = Vector2.Dot(direction, toPoint / distance);
        var outer = Fix64.Cos(Fix64.Min(outerAngle, 360) * Fix64.Half * Fix64.Deg2Rad);
        if (alignment <= outer) return Fix64.Zero;
        var innerAngleValue = Fix64.Min(innerAngle, outerAngle);
        var innerCosine = Fix64.Cos(innerAngleValue * Fix64.Half * Fix64.Deg2Rad);
        var angular = innerCosine <= outer || alignment >= innerCosine
            ? Fix64.One
            : Mathf.InverseLerp(outer, innerCosine, alignment);
        return intensity * radial * angular * angular;
    }
}
