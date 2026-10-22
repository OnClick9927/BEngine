namespace BEngine;

[AddComponentMenu("2D Animation/Chain IK 2D")]
public sealed class ChainIK2D : MonoBehaviour
{
    public Transform[] chain { get; set; } = [];
    public Transform? tip { get; set; }
    public Transform? target { get; set; }
    public bool useLimits { get; set; }
    public Fix64[] minimumAngles { get; set; } = [];
    public Fix64[] maximumAngles { get; set; } = [];
    [Range(1, 32)] public int iterations { get; set; } = 12;
    [Range(0.0001f, 1)] public Fix64 tolerance { get; set; } = Fix64.Parse("0.001");
    [Range(0, 1)] public Fix64 weight
    {
        get;
        set => field = Mathf.Clamp01(value);
    } = Fix64.One;

    public override void LateUpdate() => Solve();

    public bool Solve()
    {
        if (!enabled || weight <= 0 || target is null || tip is null || chain.Length == 0 ||
            chain.Any(bone => bone is null || bone.isDestroyed)) return false;
        var count = Math.Clamp(iterations, 1, 32);
        var threshold = Fix64.Max(Fix64.Parse("0.0001"), tolerance);
        for (var pass = 0; pass < count && Vector2.Distance(tip.position, target.position) > threshold; pass++)
        {
            for (var index = chain.Length - 1; index >= 0; index--)
            {
                var bone = chain[index];
                var toTip = tip.position - bone.position;
                var toTarget = target.position - bone.position;
                if (toTip.sqrMagnitude <= Fix64.Epsilon || toTarget.sqrMagnitude <= Fix64.Epsilon) continue;
                var delta = Mathf.Atan2(toTip.x * toTarget.y - toTip.y * toTarget.x,
                    Vector2.Dot(toTip, toTarget)) * Mathf.Rad2Deg;
                var desiredWorld = bone.rotation + delta * weight;
                if (useLimits)
                {
                    var parentRotation = bone.parent?.rotation ?? Fix64.Zero;
                    var minimum = index < minimumAngles.Length ? minimumAngles[index] : -180;
                    var maximum = index < maximumAngles.Length ? maximumAngles[index] : 180;
                    var local = NormalizeAngle(desiredWorld - parentRotation);
                    desiredWorld = parentRotation + Mathf.Clamp(local, Mathf.Min(minimum, maximum), Mathf.Max(minimum, maximum));
                }
                bone.rotation = desiredWorld;
            }
        }
        return true;
    }

    private static Fix64 NormalizeAngle(Fix64 value)
    {
        value %= 360;
        if (value > 180) value -= 360;
        else if (value <= -180) value += 360;
        return value;
    }
}
