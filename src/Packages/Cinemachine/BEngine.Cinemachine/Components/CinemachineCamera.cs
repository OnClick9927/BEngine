namespace BEngine.Cinemachine;

[AddComponentMenu("Cinemachine/Cinemachine Camera")]
public sealed class CinemachineCamera : MonoBehaviour
{
    private CameraState2D _state = CameraState2D.Default;
    private bool _stateValid;
    private Transform? _previousFollow;
    private Vector2 _previousTarget, _lookaheadVelocity;
    public Fix64 lookaheadTime { get; set; }
    public Fix64 lookaheadSmoothing { get; set; } = Fix64.Parse("0.2");

    public int priority { get; set; } = 10;
    public Transform? follow { get; set; }
    public Transform? lookAt { get; set; }
    public Vector2 trackedObjectOffset { get; set; }
    public Vector2 deadZone { get; set; }
    public Fix64 positionDamping
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.Parse("0.5");
    public Fix64 rotationDamping
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.Parse("0.5");
    public Fix64 lensDamping
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.Parse("0.5");
    public Fix64 orthographicSize
    {
        get;
        set => field = Fix64.Max(Fix64.Parse("0.001"), value);
    } = 5;

    public CameraState2D State => _state;

    public CameraState2D UpdateCameraState(Fix64 deltaTime)
    {
        var desiredPosition = follow is null
            ? transform.position
            : follow.position + follow.TransformDirection(trackedObjectOffset);
        if (follow is not null && ReferenceEquals(follow, _previousFollow) && _stateValid && deltaTime > 0)
        {
            var velocity = (follow.position - _previousTarget) / deltaTime;
            _lookaheadVelocity = Damp(_lookaheadVelocity, velocity,
                Fix64.Max(0, lookaheadSmoothing), deltaTime);
            desiredPosition += _lookaheadVelocity * Fix64.Max(0, lookaheadTime);
        }
        else _lookaheadVelocity = Vector2.zero;
        _previousFollow = follow;
        _previousTarget = follow?.position ?? Vector2.zero;
        if (_stateValid && follow is not null)
        {
            var offset = desiredPosition - _state.RawPosition;
            var halfDeadZone = new Vector2(
                Fix64.Abs(deadZone.x) * Fix64.Half,
                Fix64.Abs(deadZone.y) * Fix64.Half);
            desiredPosition = _state.RawPosition + new Vector2(
                Fix64.Abs(offset.x) <= halfDeadZone.x
                    ? Fix64.Zero
                    : offset.x - Mathf.Sign(offset.x) * halfDeadZone.x,
                Fix64.Abs(offset.y) <= halfDeadZone.y
                    ? Fix64.Zero
                    : offset.y - Mathf.Sign(offset.y) * halfDeadZone.y);
        }

        var hasLookAt = lookAt is not null;
        var referenceLookAt = lookAt?.position ?? Vector2.zero;
        var desiredRotation = transform.rotation;
        if (hasLookAt)
        {
            var direction = referenceLookAt - desiredPosition;
            if (direction.sqrMagnitude > Fix64.Epsilon)
                desiredRotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        var confiner = GetComponent<CinemachineConfiner2D>();
        if (confiner is { enabled: true }) desiredPosition = confiner.ConstrainPoint(desiredPosition);

        if (!_stateValid || deltaTime <= Fix64.Zero)
        {
            _state = new CameraState2D(desiredPosition, desiredRotation, orthographicSize,
                Vector2.zero, Fix64.Zero, referenceLookAt, hasLookAt);
            _stateValid = true;
            return _state;
        }

        _state = new CameraState2D(
            Damp(_state.RawPosition, desiredPosition, positionDamping, deltaTime),
            DampAngle(_state.RawRotation, desiredRotation, rotationDamping, deltaTime),
            Damp(_state.OrthographicSize, orthographicSize, lensDamping, deltaTime),
            Vector2.zero,
            Fix64.Zero,
            referenceLookAt,
            hasLookAt);
        return _state;
    }

    public void ForceCameraPosition(Vector2 position, Fix64 rotation)
    {
        _previousFollow = null;
        _state = new CameraState2D(position, rotation, orthographicSize,
            Vector2.zero, Fix64.Zero, lookAt?.position ?? Vector2.zero, lookAt is not null);
        _stateValid = true;
    }

    /// <summary>Clears damping and lookahead history after a cut or a target teleport.</summary>
    public void ResetState()
    {
        _stateValid = false;
        _previousFollow = null;
        _lookaheadVelocity = Vector2.zero;
    }

    private static Fix64 Damp(Fix64 current, Fix64 target, Fix64 damping, Fix64 deltaTime) =>
        damping <= Fix64.Epsilon ? target : Mathf.Lerp(current, target, deltaTime / (damping + deltaTime));

    private static Vector2 Damp(Vector2 current, Vector2 target, Fix64 damping, Fix64 deltaTime) =>
        damping <= Fix64.Epsilon ? target : Vector2.Lerp(current, target, deltaTime / (damping + deltaTime));

    private static Fix64 DampAngle(Fix64 current, Fix64 target, Fix64 damping, Fix64 deltaTime)
    {
        var delta = Mathf.Repeat(target - current + 180, 360) - 180;
        return current + Damp(Fix64.Zero, delta, damping, deltaTime);
    }
}
