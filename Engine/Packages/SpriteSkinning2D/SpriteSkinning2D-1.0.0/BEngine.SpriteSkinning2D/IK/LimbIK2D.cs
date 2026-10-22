namespace BEngine;

[AddComponentMenu("2D Animation/Limb IK 2D")]
public sealed class LimbIK2D : MonoBehaviour
{
    public Transform? root { get; set; }
    public Transform? middle { get; set; }
    public Transform? tip { get; set; }
    public Transform? target { get; set; }
    public bool bendPositive { get; set; } = true;
    [Range(0, 1)] public Fix64 weight
    {
        get;
        set => field = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
    } = Fix64.One;

    public override void LateUpdate() => Solve();

    public bool Solve()
    {
        if (!enabled || weight <= Fix64.Zero || root is null || middle is null || tip is null || target is null)
            return false;
        var rootPosition = root.position;
        var firstLength = Vector2.Distance(rootPosition, middle.position);
        var secondLength = Vector2.Distance(middle.position, tip.position);
        if (firstLength <= Fix64.Epsilon || secondLength <= Fix64.Epsilon) return false;
        var toTarget = target.position - rootPosition;
        var distance = toTarget.magnitude;
        if (distance <= Fix64.Epsilon) toTarget = root.right;
        distance = Fix64.Clamp(distance, Fix64.Parse("0.0001"),
            Fix64.Max(Fix64.Parse("0.0001"), firstLength + secondLength - Fix64.Parse("0.0001")));
        var baseAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Fix64.Rad2Deg;
        var rootOffset = Acos(Fix64.Clamp(
            (firstLength * firstLength + distance * distance - secondLength * secondLength) /
            (2 * firstLength * distance), -Fix64.One, Fix64.One)) * Fix64.Rad2Deg;
        var middleInterior = Acos(Fix64.Clamp(
            (firstLength * firstLength + secondLength * secondLength - distance * distance) /
            (2 * firstLength * secondLength), -Fix64.One, Fix64.One)) * Fix64.Rad2Deg;
        var sign = bendPositive ? Fix64.One : -Fix64.One;
        var solvedRoot = baseAngle + rootOffset * sign;
        var solvedMiddle = solvedRoot - (180 - middleInterior) * sign;
        root.rotation = LerpAngle(root.rotation, solvedRoot, weight);
        middle.rotation = LerpAngle(middle.rotation, solvedMiddle, weight);
        return true;
    }

    private static Fix64 LerpAngle(Fix64 from, Fix64 to, Fix64 amount)
    {
        var delta = (to - from) % 360;
        if (delta > 180) delta -= 360;
        else if (delta <= -180) delta += 360;
        return from + delta * amount;
    }

    private static Fix64 Acos(Fix64 value)
    {
        value = Fix64.Clamp(value, -Fix64.One, Fix64.One);
        return Mathf.Atan2(Fix64.Sqrt(Fix64.Max(Fix64.Zero, Fix64.One - value * value)), value);
    }
}
