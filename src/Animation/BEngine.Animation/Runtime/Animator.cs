using System.Reflection;

namespace BEngine.Animation;

[DisallowMultipleComponent]
[AddComponentMenu("Animation/Animator")]
public sealed class Animator : Behaviour
{
    private readonly Dictionary<string, Fix64> _floats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _bools = new(StringComparer.Ordinal);
    private readonly HashSet<string> _triggers = new(StringComparer.Ordinal);
    private AnimatorController? _loadedController;
    private string? _loadedPath;
    private AnimatorState? _state;
    private Fix64 _stateTime;
    private Fix64 _previousTime;

    public string controllerPath { get; set; } = string.Empty;
    [HideInInspector]
    public AnimatorController? runtimeAnimatorController { get; set; }
    public Fix64 speed { get; set; } = Fix64.One;
    public bool applyRootMotion { get; set; }
    public bool playOnAwake { get; set; } = true;
    public string currentStateName => _state?.name ?? string.Empty;
    public Fix64 normalizedTime
    {
        get
        {
            var clip = _state?.ResolveClip();
            return clip is not null && clip.length > Fix64.Zero ? _stateTime / clip.length : Fix64.Zero;
        }
    }

    public void Play(string stateName, int layer = -1, Fix64 normalizedTime = default)
    {
        var controller = ResolveController();
        var state = controller?.FindState(stateName);
        if (state is null) return;
        _state = state;
        var clip = state.ResolveClip();
        _stateTime = clip is null ? Fix64.Zero : clip.length * normalizedTime;
        _previousTime = _stateTime;
    }
    public void CrossFade(string stateName, Fix64 normalizedTransitionDuration, int layer = -1, Fix64 normalizedTimeOffset = default) => Play(stateName, layer, normalizedTimeOffset);
    public bool HasState(int layerIndex, string stateName) => ResolveController()?.FindState(stateName) is not null;
    public void SetFloat(string name, Fix64 value) => _floats[name] = value;
    public Fix64 GetFloat(string name) => _floats.GetValueOrDefault(name);
    public void SetInteger(string name, int value) => _ints[name] = value;
    public int GetInteger(string name) => _ints.GetValueOrDefault(name);
    public void SetBool(string name, bool value) => _bools[name] = value;
    public bool GetBool(string name) => _bools.GetValueOrDefault(name);
    public void SetTrigger(string name) => _triggers.Add(name);
    public void ResetTrigger(string name) => _triggers.Remove(name);
    public bool IsInTransition(int layerIndex) => false;

    internal void Tick(Fix64 deltaTime)
    {
        if (!enabled) return;
        var controller = ResolveController();
        if (controller is null) return;
        if (_state is null)
        {
            if (!playOnAwake) return;
            InitializeParameters(controller);
            Play(controller.defaultState);
        }
        if (_state is null) return;
        var clip = _state.ResolveClip();
        if (clip is null) return;
        _previousTime = _stateTime;
        _stateTime += deltaTime * speed * _state.speed;
        var normalized = clip.length > 0 ? _stateTime / clip.length : Fix64.One;
        var transition = _state.transitions.FirstOrDefault(item =>
            (!item.hasExitTime || normalized >= item.exitTime) && item.conditions.All(Evaluate));
        if (transition is not null)
        {
            foreach (var condition in transition.conditions.Where(item =>
                         controller.parameters.Any(parameter => parameter.name == item.parameter && parameter.type == AnimatorControllerParameterType.Trigger)))
                _triggers.Remove(condition.parameter);
            Play(transition.destinationState);
            clip = _state?.ResolveClip() ?? clip;
        }
        var sampleTime = Wrap(_stateTime, clip.length, _state?.loop == true || clip.wrapMode == WrapMode.Loop);
        AnimationSampler.Sample(clip, gameObject, sampleTime);
        DispatchEvents(clip, _previousTime, _stateTime);
    }

    private AnimatorController? ResolveController()
    {
        if (runtimeAnimatorController is not null) return runtimeAnimatorController;
        if (string.IsNullOrWhiteSpace(controllerPath)) return null;
        var path = AssetPath.Resolve(controllerPath);
        if (_loadedController is null || _loadedPath != path)
        {
            if (!File.Exists(path)) return null;
            _loadedController = AnimatorController.Load(path);
            _loadedPath = path;
            _state = null;
        }
        return _loadedController;
    }

    private void InitializeParameters(AnimatorController controller)
    {
        foreach (var parameter in controller.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Float) _floats.TryAdd(parameter.name, parameter.defaultFloat);
            else if (parameter.type == AnimatorControllerParameterType.Int) _ints.TryAdd(parameter.name, parameter.defaultInt);
            else if (parameter.type == AnimatorControllerParameterType.Bool) _bools.TryAdd(parameter.name, parameter.defaultBool);
        }
    }

    private bool Evaluate(AnimatorCondition condition)
    {
        if (_triggers.Contains(condition.parameter)) return condition.mode != AnimatorConditionMode.IfNot;
        if (_bools.TryGetValue(condition.parameter, out var boolean))
            return condition.mode == AnimatorConditionMode.If ? boolean : condition.mode == AnimatorConditionMode.IfNot && !boolean;
        var value = _floats.TryGetValue(condition.parameter, out var floating) ? floating : (Fix64)_ints.GetValueOrDefault(condition.parameter);
        return condition.mode switch
        {
            AnimatorConditionMode.Greater => value > condition.threshold,
            AnimatorConditionMode.Less => value < condition.threshold,
            AnimatorConditionMode.Equals => value == condition.threshold,
            AnimatorConditionMode.NotEqual => value != condition.threshold,
            _ => value != Fix64.Zero
        };
    }

    private void DispatchEvents(AnimationClip clip, Fix64 previous, Fix64 current)
    {
        foreach (var animationEvent in clip.events.Where(item => item.time > previous && item.time <= current))
        foreach (var component in gameObject.components)
        {
            if (component is not MonoBehaviour { enabled: true } behaviour) continue;
            try
            {
                if (!RuntimeTypeCache.TryInvokeMessage(behaviour, animationEvent.functionName,
                        animationEvent))
                    RuntimeTypeCache.TryInvokeMessage(behaviour, animationEvent.functionName);
            }
            catch (Exception exception)
            {
                Debug.LogError($"{behaviour.GetType().FullName}.{animationEvent.functionName} failed: " +
                               exception.Message);
            }
        }
    }

    private static Fix64 Wrap(Fix64 time, Fix64 length, bool loop) =>
        length <= 0 ? Fix64.Zero : loop ? time % length : Mathf.Min(time, length);
}
