namespace BEngine.Physics2D;

[AddComponentMenu("Physics 2D/Friction Joint 2D")]
public sealed class FrictionJoint2D : Joint2D
{
    public Fix64 maxForce { get; set; } = 10;
    public Fix64 maxTorque { get; set; } = 10;
    internal override void Prepare(Fix64 dt)
    {
        base.Prepare(dt);
        var inverse = InverseMass(body) + InverseMass(connectedBody);
        if (inverse > 0) DriveLinear(Vector2.ClampMagnitude(-(body.linearVelocity - (connectedBody?.linearVelocity ?? Vector2.zero)) / inverse,
            Fix64.Max(0, maxForce) * dt), dt);
        Motor(0, maxTorque, dt);
    }
    internal override void Solve(Fix64 dt) { }
}

[AddComponentMenu("Physics 2D/Target Joint 2D")]
public sealed class TargetJoint2D : Joint2D
{
    public Vector2 target { get; set; }
    public Fix64 maxForce { get; set; } = 100;
    public Fix64 frequency { get; set; } = 5;
    public Fix64 dampingRatio { get; set; } = (Fix64).7;
    internal override void Prepare(Fix64 dt)
    {
        base.Prepare(dt); var inverse = InverseMass(body); if (inverse <= 0 || dt <= 0) return;
        var omega = Fix64.Min(Fix64.Max(0, frequency) * 2 * Mathf.PI, Fix64.Half / dt);
        var error = target - worldAnchor;
        if (error.magnitude > Fix64.Epsilon)
            omega = Fix64.Min(omega, Mathf.Sqrt(Fix64.Max(0, maxForce) * inverse / error.magnitude));
        var damping = 2 * Mathf.Clamp01(dampingRatio) * omega;
        // Implicit spring integration remains stable when the configured force cap
        // makes the requested acceleration smaller than the spring acceleration.
        var force = (error * omega * omega - body.GetPointVelocity(worldAnchor) * damping)
            / (inverse * (1 + damping * dt + omega * omega * dt * dt));
        force = Vector2.ClampMagnitude(force, Fix64.Max(0, maxForce));
        if (SetReaction(force)) body.AddForceAtPosition(force, worldAnchor);
    }
    internal override void Solve(Fix64 dt) { }
}

[AddComponentMenu("Physics 2D/Relative Joint 2D")]
public sealed class RelativeJoint2D : Joint2D
{
    public Vector2 linearOffset { get; set; }
    public Fix64 angularOffset { get; set; }
    public Fix64 maxForce { get; set; } = 100;
    public Fix64 maxTorque { get; set; } = 100;
    public Fix64 correctionScale { get; set; } = (Fix64).3;
    internal override void Prepare(Fix64 dt)
    {
        base.Prepare(dt); if (dt <= 0) return;
        var radians = (connectedBody?.rotation ?? 0) * Mathf.Deg2Rad;
        var rotated = new Vector2(linearOffset.x * Mathf.Cos(radians) - linearOffset.y * Mathf.Sin(radians),
            linearOffset.x * Mathf.Sin(radians) + linearOffset.y * Mathf.Cos(radians));
        var target = (connectedBody?.position ?? Vector2.zero) + rotated;
        var inverse = InverseMass(body) + InverseMass(connectedBody);
        if (inverse > 0)
        {
            var desired = (target - body.position) * Mathf.Clamp01(correctionScale) / dt;
            var impulse = (desired - (body.linearVelocity - (connectedBody?.linearVelocity ?? Vector2.zero))) / inverse;
            DriveLinear(Vector2.ClampMagnitude(impulse, Fix64.Max(0, maxForce) * dt), dt);
        }
        var angle = ((connectedBody?.rotation ?? 0) + angularOffset - body.rotation) % 360;
        if (angle > 180) angle -= 360; if (angle < -180) angle += 360;
        Motor(angle * Mathf.Clamp01(correctionScale) / dt, maxTorque, dt);
    }
    internal override void Solve(Fix64 dt) { }
}

[AddComponentMenu("Physics 2D/Wheel Joint 2D")]
public sealed class WheelJoint2D : Joint2D
{
    public Fix64 suspensionAngle { get; set; } = 90;
    public Fix64 suspensionDistance { get; set; }
    public Fix64 frequency { get; set; } = 2;
    public Fix64 dampingRatio { get; set; } = (Fix64).7;
    public bool useMotor { get; set; }
    public Fix64 motorSpeed { get; set; }
    public Fix64 maxMotorTorque { get; set; } = 100;
    private Vector2 Axis => new(Mathf.Cos(suspensionAngle * Mathf.Deg2Rad), Mathf.Sin(suspensionAngle * Mathf.Deg2Rad));
    internal override void Prepare(Fix64 dt)
    {
        base.Prepare(dt); if (dt <= 0) return;
        var inverse = InverseMass(body) + InverseMass(connectedBody);
        if (inverse > 0)
        {
            var axis = Axis; var omega = Fix64.Min(Fix64.Max(0, frequency) * 2 * Mathf.PI, Fix64.Half / dt);
            var error = Vector2.Dot(worldAnchor - worldConnectedAnchor, axis) - suspensionDistance;
            var speed = Vector2.Dot(body.linearVelocity - (connectedBody?.linearVelocity ?? Vector2.zero), axis);
            var force = -axis * (omega * omega * error + 2 * Mathf.Clamp01(dampingRatio) * omega * speed) / inverse;
            if (SetReaction(force)) { body.AddForceAtPosition(force, worldAnchor); connectedBody?.AddForceAtPosition(-force, worldConnectedAnchor); }
        }
        if (useMotor) Motor(motorSpeed, maxMotorTorque, dt);
    }
    internal override void Solve(Fix64 dt)
    {
        var axis = Axis; var normal = new Vector2(-axis.y, axis.x);
        Correct(normal * Vector2.Dot(worldAnchor - worldConnectedAnchor, normal), normal, dt);
    }
}
