namespace BEngine.Cinemachine;

[RequireComponent(typeof(Camera2D))]
[DisallowMultipleComponent]
[AddComponentMenu("Cinemachine/Cinemachine Brain")]
public sealed class CinemachineBrain : MonoBehaviour
{
    private CinemachineCamera? _activeCamera;
    private CinemachineCamera? _outgoingCamera;
    private CameraState2D _blendFrom;
    private CinemachineBlendDefinition _blendDefinition;
    private Fix64 _blendElapsed;

    public CinemachineBlendDefinition defaultBlend { get; set; } =
        new(CinemachineBlendStyle.EaseInOut, Fix64.One);
    public CinemachineBlenderSettings? customBlends { get; set; }
    public bool ignoreTimeScale { get; set; }

    public CinemachineCamera? activeVirtualCamera => _activeCamera;
    public CinemachineBlend? activeBlend => _activeCamera is null ||
        _blendDefinition.Evaluate(_blendElapsed) >= Fix64.One
            ? null
            : new CinemachineBlend(_outgoingCamera, _activeCamera, _blendDefinition, _blendElapsed);

    public event Action<CinemachineCamera?, CinemachineCamera>? cameraActivated;

    public override void LateUpdate()
    {
        var scene = gameObject.scene;
        if (scene is null) return;
        var cameras = scene.QueryComponents<CinemachineCamera>();
        CinemachineCamera? selected = null;
        var selectedPriority = 0;
        var selectedId = 0;
        for (var index = 0; index < cameras.Count; index++)
        {
            var camera = cameras[index];
            if (!camera.enabled || !camera.gameObject.activeInHierarchy) continue;
            var priority = camera.priority;
            var id = camera.GetInstanceID();
            if (selected is not null && (priority < selectedPriority ||
                priority == selectedPriority && id >= selectedId)) continue;
            selected = camera;
            selectedPriority = priority;
            selectedId = id;
        }
        if (selected is null)
        {
            _activeCamera = null;
            _outgoingCamera = null;
            return;
        }

        var deltaTime = ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        var state = selected.UpdateCameraState(deltaTime);
        if (!ReferenceEquals(selected, _activeCamera))
        {
            var previous = _activeCamera;
            _outgoingCamera = previous;
            var output = GetComponent<Camera2D>()!;
            _blendFrom = new CameraState2D(output.transform.position, output.transform.rotation,
                output.size, Vector2.zero, Fix64.Zero, Vector2.zero, false);
            _blendDefinition = previous is null
                ? new CinemachineBlendDefinition(CinemachineBlendStyle.Cut, Fix64.Zero)
                : customBlends?.GetBlendFor(previous, selected, defaultBlend) ?? defaultBlend;
            _blendElapsed = Fix64.Zero;
            _activeCamera = selected;
            cameraActivated?.Invoke(previous, selected);
            if (!ReferenceEquals(_activeCamera, selected) || !enabled) return;
        }

        _blendElapsed += Fix64.Max(Fix64.Zero, deltaTime);
        var blendWeight = _blendDefinition.Evaluate(_blendElapsed);
        var result = CameraState2D.Lerp(_blendFrom, state, blendWeight);
        if (blendWeight >= Fix64.One) _outgoingCamera = null;
        var listener = GetComponent<CinemachineImpulseListener>();
        if (listener is { enabled: true })
            result = result with { PositionCorrection = result.PositionCorrection + listener.Evaluate(scene) };
        Apply(result);
    }

    public bool IsLive(CinemachineCamera camera) => ReferenceEquals(_activeCamera, camera) ||
        ReferenceEquals(_outgoingCamera, camera) && activeBlend is not null;

    public void CutTo(CinemachineCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _activeCamera = camera;
        _outgoingCamera = null;
        _blendDefinition = new CinemachineBlendDefinition(CinemachineBlendStyle.Cut, Fix64.Zero);
        _blendElapsed = Fix64.Zero;
        Apply(camera.UpdateCameraState(Fix64.Zero));
    }

    private void Apply(CameraState2D state)
    {
        var output = GetComponent<Camera2D>()!;
        output.transform.SetPositionAndRotation(state.FinalPosition, state.FinalRotation);
        output.size = state.OrthographicSize;
    }
}
