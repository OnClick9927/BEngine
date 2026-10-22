namespace BEngine;

[AddComponentMenu("2D Animation/Transform Constraint 2D")]
public sealed class TransformConstraint2D : MonoBehaviour
{
    public Transform? target { get; set; }
    public bool constrainPosition { get; set; }
    public bool constrainRotation { get; set; } = true;
    public bool constrainScale { get; set; }
    public Vector2 positionOffset { get; set; }
    public Fix64 rotationOffset { get; set; }
    public Vector2 scaleMultiplier { get; set; } = Vector2.one;
    [Range(0, 1)] public Fix64 weight
    {
        get;
        set => field = Mathf.Clamp01(value);
    } = Fix64.One;

    public override void LateUpdate() => Apply();

    public bool Apply()
    {
        if (!enabled || target is null || target.isDestroyed || weight <= 0) return false;
        if (constrainPosition)
            transform.position = Vector2.Lerp(transform.position,
                target.TransformPoint(positionOffset), weight);
        if (constrainRotation)
            transform.rotation = LerpAngle(transform.rotation, target.rotation + rotationOffset, weight);
        if (constrainScale)
        {
            var desired = new Vector2(target.lossyScale.x * scaleMultiplier.x,
                target.lossyScale.y * scaleMultiplier.y);
            var parentScale = transform.parent?.lossyScale ?? Vector2.one;
            desired = new Vector2(parentScale.x == 0 ? transform.localScale.x : desired.x / parentScale.x,
                parentScale.y == 0 ? transform.localScale.y : desired.y / parentScale.y);
            transform.localScale = Vector2.Lerp(transform.localScale, desired, weight);
        }
        return true;
    }

    private static Fix64 LerpAngle(Fix64 from, Fix64 to, Fix64 amount)
    {
        var delta = (to - from) % 360;
        if (delta > 180) delta -= 360;
        else if (delta <= -180) delta += 360;
        return from + delta * amount;
    }
}
