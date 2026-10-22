namespace BEngine.Physics2D;

[RequireComponent(typeof(Rigidbody2D))]
public abstract class Joint2D : Component
{
    public Rigidbody2D? connectedBody { get; set; }
    public Vector2 anchor { get; set; }
    /// <summary>Local to connectedBody, or world coordinates when no body is connected.</summary>
    public Vector2 connectedAnchor { get; set; }
    public bool enableCollision { get; set; }
    public Fix64 breakForce { get; set; } = Fix64.FromRaw(long.MaxValue);
    public Fix64 breakTorque { get; set; } = Fix64.FromRaw(long.MaxValue);
    [HideInInspector] public Fix64 reactionTorque { get; private set; }
    [HideInInspector] public bool broken { get; private set; }
    [HideInInspector] public Vector2 reactionForce { get; private set; }
    protected Rigidbody2D body => GetComponent<Rigidbody2D>()!;
    protected Vector2 worldAnchor => transform.TransformPoint(anchor);
    protected Vector2 worldConnectedAnchor => connectedBody?.transform.TransformPoint(connectedAnchor) ?? connectedAnchor;
    internal bool canSolve => !broken && enabled && gameObject.activeInHierarchy && body is { simulated: true } &&
        (connectedBody is null || !connectedBody.isDestroyed && connectedBody.simulated && ReferenceEquals(connectedBody.gameObject.scene, gameObject.scene)) &&
        !ReferenceEquals(body, connectedBody);
    internal virtual void Prepare(Fix64 deltaTime) { reactionForce = Vector2.zero; reactionTorque = 0; }
    protected void DriveLinear(Vector2 impulse, Fix64 deltaTime)
    {
        if (!canSolve || deltaTime <= 0 || !SetReaction(impulse / deltaTime)) return;
        body.linearVelocity += Constrain(impulse * InverseMass(body), body.constraints);
        if (connectedBody is { } other) other.linearVelocity -= Constrain(impulse * InverseMass(other), other.constraints);
        body.WakeUp(); connectedBody?.WakeUp();
    }
    protected void DriveAngular(Fix64 impulse, Fix64 deltaTime)
    {
        if (!canSolve || deltaTime <= 0 || !SetTorque(impulse / deltaTime)) return;
        body.angularVelocity += impulse * InverseInertia(body) * Mathf.Rad2Deg;
        if (connectedBody is { } other) other.angularVelocity -= impulse * InverseInertia(other) * Mathf.Rad2Deg;
        body.WakeUp(); connectedBody?.WakeUp();
    }
    protected void Motor(Fix64 speed, Fix64 torque, Fix64 deltaTime)
    {
        var inverse = InverseInertia(body) + InverseInertia(connectedBody); if (inverse <= 0) return;
        var impulse = (speed - (body.angularVelocity - (connectedBody?.angularVelocity ?? 0))) * Mathf.Deg2Rad / inverse;
        DriveAngular(Mathf.Clamp(impulse, -Fix64.Max(0, torque) * deltaTime, Fix64.Max(0, torque) * deltaTime), deltaTime);
    }
    internal abstract void Solve(Fix64 deltaTime);
    protected static Fix64 InverseMass(Rigidbody2D? value) => value is { simulated: true, bodyType: RigidbodyType2D.Dynamic } ? Fix64.One / value.mass : Fix64.Zero;
    protected static Fix64 InverseInertia(Rigidbody2D? value) => InverseMass(value) > 0 &&
        !value!.constraints.HasFlag(RigidbodyConstraints2D.FreezeRotation) ? Fix64.One / value.inertia : Fix64.Zero;

    protected void Correct(Vector2 error, Vector2? axis, Fix64 deltaTime)
    {
        if (!canSolve) return;
        var a = InverseMass(body); var b = InverseMass(connectedBody); var sum = a + b;
        if (sum <= Fix64.Zero || deltaTime <= Fix64.Zero) return;
        if (!SetReaction(error / (sum * deltaTime * deltaTime))) return;
        var correction = error / sum;
        var velocity = body.linearVelocity - (connectedBody?.linearVelocity ?? Vector2.zero);
        if (axis is { } normal) velocity = normal * Vector2.Dot(velocity, normal);
        if (a > Fix64.Zero)
        {
            body.position -= Constrain(correction * a, body.constraints);
            body.linearVelocity -= Constrain(velocity * (a / sum), body.constraints); body.WakeUp();
        }
        if (b > Fix64.Zero)
        {
            connectedBody!.position += Constrain(correction * b, connectedBody.constraints);
            connectedBody.linearVelocity += Constrain(velocity * (b / sum), connectedBody.constraints); connectedBody.WakeUp();
        }
    }

    protected bool SetReaction(Vector2 force)
    {
        reactionForce = force;
        if (reactionForce.magnitude > Fix64.Max(Fix64.Zero, breakForce))
        {
            Break();
            return false;
        }
        return true;
    }

    private bool SetTorque(Fix64 torque)
    {
        reactionTorque = torque;
        if (Fix64.Abs(torque) <= Fix64.Max(0, breakTorque)) return true;
        Break(); return false;
    }
    private void Break()
    {
        if (broken) return;
        broken = true;
        foreach (var receiver in gameObject.GetComponents<MonoBehaviour>())
            if (receiver.enabled)
                try { RuntimeTypeCache.TryInvokeMessage(receiver, "OnJointBreak2D", this); }
                catch (Exception error) { Debug.LogException(error); }
    }
    protected void CorrectRotation(Fix64 error, Fix64 deltaTime)
    {
        if (!canSolve || error == Fix64.Zero) return;
        var a = InverseInertia(body);
        var b = InverseInertia(connectedBody);
        var sum = a + b; if (sum <= Fix64.Zero || deltaTime <= 0) return;
        if (!SetTorque(error * Mathf.Deg2Rad / (sum * deltaTime * deltaTime))) return;
        var speed = body.angularVelocity - (connectedBody?.angularVelocity ?? Fix64.Zero);
        if (a > Fix64.Zero) { body.rotation -= error * a / sum; body.angularVelocity -= speed * a / sum; }
        if (b > Fix64.Zero) { connectedBody!.rotation += error * b / sum; connectedBody.angularVelocity += speed * b / sum; }
    }
    private static Vector2 Constrain(Vector2 value, RigidbodyConstraints2D constraints) => new(
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionX) ? Fix64.Zero : value.x,
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionY) ? Fix64.Zero : value.y);
}

[AddComponentMenu("Physics 2D/Distance Joint 2D")]
public sealed class DistanceJoint2D : Joint2D
{
    public Fix64 distance { get; set; } = Fix64.One;
    public bool maxDistanceOnly { get; set; }
    internal override void Solve(Fix64 deltaTime)
    {
        var delta = worldAnchor - worldConnectedAnchor; var length = delta.magnitude;
        var error = length - Fix64.Max(Fix64.Zero, distance);
        if (maxDistanceOnly && error <= Fix64.Zero) return;
        var normal = length <= Fix64.Epsilon ? Vector2.right : delta / length;
        Correct(normal * error, normal, deltaTime);
    }
}

[AddComponentMenu("Physics 2D/Spring Joint 2D")]
public sealed class SpringJoint2D : Joint2D
{
    public Fix64 distance { get; set; } = Fix64.One;
    public Fix64 frequency { get; set; } = 2;
    public Fix64 dampingRatio { get; set; } = Fix64.FromDecimal(0.5m);
    internal override void Prepare(Fix64 deltaTime)
    {
        base.Prepare(deltaTime);
        var delta = worldAnchor - worldConnectedAnchor; var length = delta.magnitude;
        if (length <= Fix64.Epsilon) return;
        var invMass = InverseMass(body) + InverseMass(connectedBody); if (invMass <= Fix64.Zero) return;
        var axis = delta / length;
        // Limit frequency relative to the integration step to keep the explicit solver stable.
        var omega = Mathf.Min(Fix64.Max(0, frequency) * 2 * Mathf.PI, Fix64.Half / deltaTime);
        var speed = Vector2.Dot(body.linearVelocity - (connectedBody?.linearVelocity ?? Vector2.zero), axis);
        var force = -axis * (omega * omega * (length - Fix64.Max(0, distance)) + 2 * Mathf.Clamp01(dampingRatio) * omega * speed) / invMass;
        if (!SetReaction(force)) return;
        body.AddForce(force); connectedBody?.AddForce(-force);
    }
    internal override void Solve(Fix64 deltaTime) { }
}

[AddComponentMenu("Physics 2D/Hinge Joint 2D")]
public sealed class HingeJoint2D : Joint2D
{
    public bool useLimits { get; set; }
    public Fix64 minAngle { get; set; } = -90;
    public Fix64 maxAngle { get; set; } = 90;
    public bool useMotor { get; set; }
    public Fix64 motorSpeed { get; set; }
    public Fix64 maxMotorTorque { get; set; } = 100;
    internal override void Prepare(Fix64 deltaTime)
    {
        base.Prepare(deltaTime);
        if (!useMotor) return;
        Motor(motorSpeed, maxMotorTorque, deltaTime);
    }
    internal override void Solve(Fix64 deltaTime)
    {
        Correct(worldAnchor - worldConnectedAnchor, null, deltaTime);
        if (useLimits)
        {
            var angle = body.rotation - (connectedBody?.rotation ?? Fix64.Zero);
            CorrectRotation(angle - Mathf.Clamp(angle, Mathf.Min(minAngle, maxAngle), Mathf.Max(minAngle, maxAngle)), deltaTime);
        }
    }
}

[AddComponentMenu("Physics 2D/Fixed Joint 2D")]
public sealed class FixedJoint2D : Joint2D
{
    public Fix64 referenceAngle { get; set; }
    internal override void Solve(Fix64 deltaTime)
    {
        Correct(worldAnchor - worldConnectedAnchor, null, deltaTime);
        CorrectRotation(body.rotation - (connectedBody?.rotation ?? Fix64.Zero) - referenceAngle, deltaTime);
    }
}

[AddComponentMenu("Physics 2D/Slider Joint 2D")]
public sealed class SliderJoint2D : Joint2D
{
    public bool useMotor { get; set; }
    public Fix64 motorSpeed { get; set; }
    public Fix64 maxMotorForce { get; set; } = 100;
    internal override void Prepare(Fix64 deltaTime)
    {
        base.Prepare(deltaTime); if (!useMotor) return;
        var radians = angle * Mathf.Deg2Rad; var axis = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        var inverse = InverseMass(body) + InverseMass(connectedBody); if (inverse <= 0) return;
        var speed = Vector2.Dot(body.linearVelocity - (connectedBody?.linearVelocity ?? Vector2.zero), axis);
        DriveLinear(axis * Mathf.Clamp((motorSpeed - speed) / inverse, -Fix64.Max(0, maxMotorForce) * deltaTime,
            Fix64.Max(0, maxMotorForce) * deltaTime), deltaTime);
    }
    public Fix64 angle { get; set; }
    public bool useLimits { get; set; }
    public Fix64 minDistance { get; set; } = -1;
    public Fix64 maxDistance { get; set; } = 1;
    internal override void Solve(Fix64 deltaTime)
    {
        var radians = angle * Mathf.Deg2Rad;
        var axis = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        var normal = new Vector2(-axis.y, axis.x); var delta = worldAnchor - worldConnectedAnchor;
        Correct(normal * Vector2.Dot(delta, normal), normal, deltaTime);
        if (useLimits)
        {
            var along = Vector2.Dot(delta, axis);
            var error = along - Mathf.Clamp(along, Mathf.Min(minDistance, maxDistance), Mathf.Max(minDistance, maxDistance));
            if (error != Fix64.Zero) Correct(axis * error, axis, deltaTime);
        }
    }
}
