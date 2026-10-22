using System.Reflection;

namespace BEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Animation/Animator")]
public sealed partial class Animator : Behaviour
{
    /// <summary>Read-only information about the state currently evaluated by a layer.</summary>
    public readonly record struct PlaybackInfo(
        string StateName,
        string? SourceStateName,
        Fix64 NormalizedTime,
        Fix64 TransitionNormalizedTime,
        bool InTransition,
        bool HasMotion);

    private readonly Dictionary<string, Fix64> _floats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _bools = new(StringComparer.Ordinal);
    private readonly HashSet<string> _triggers = new(StringComparer.Ordinal);
    private sealed class Playback
    {
        public AnimatorState? State, FromState;
        public string StatePath = "", FromPath = "";
        public Fix64 StateTime, PreviousTime, FromTime, TransitionDuration, TransitionElapsed;
        public bool EventStartPending, Exited;
    }
    private readonly Dictionary<int, Playback> _playbacks = [];
    private readonly Dictionary<int, Fix64> _layerWeights = [];
    private Playback _playback = new();
    private int _activeLayer;
    private Fix64 _layerWeight = Fix64.One;
    private AnimatorState? _state { get => _playback.State; set => _playback.State = value; }
    private Fix64 _stateTime { get => _playback.StateTime; set => _playback.StateTime = value; }
    private Fix64 _previousTime { get => _playback.PreviousTime; set => _playback.PreviousTime = value; }
    private bool _eventStartPending { get => _playback.EventStartPending; set => _playback.EventStartPending = value; }
    private AnimatorState? _fromState { get => _playback.FromState; set => _playback.FromState = value; }
    private Fix64 _fromTime { get => _playback.FromTime; set => _playback.FromTime = value; }
    private Fix64 _transitionDuration { get => _playback.TransitionDuration; set => _playback.TransitionDuration = value; }
    private Fix64 _transitionElapsed { get => _playback.TransitionElapsed; set => _playback.TransitionElapsed = value; }
    private AnimatorController? _effectiveController;
    private int _playVersion;
    private readonly AnimatorPose _pose = new();
    private readonly List<(AnimationClip Clip, Fix64 Time, Fix64 Weight)> _samples = [];
    // A blend tree can be resolved several times while one layer is evaluated
    // (length, root motion, transition and sampling). Keep that result for the
    // current Tick only; authored trees may be edited between ticks.
    private readonly Dictionary<AnimatorState, MotionSet> _motionCache = [];
    private readonly List<List<(AnimationClip Clip, Fix64 Weight)>> _motionBuffers = [];
    private int _motionLeaseCursor;
    private bool _motionCacheActive;
    private readonly Func<string, Fix64> _readBlendParameter;
    private readonly List<ParameterPlaybackLength> _parameterChanges = [];

    public Animator() => _readBlendParameter = ReadBlendParameter;

    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string controllerPath { get => runtimeAnimatorController?.assetPath ?? string.Empty; set => runtimeAnimatorController = BAsset.Load<AnimatorController>(value); }
    [FormerlySerializedAs("controllerPath")]
    public AnimatorController? runtimeAnimatorController { get; set; }
    public Fix64 speed { get; set; } = Fix64.One;
    public bool applyRootMotion { get; set; }
    public Vector2 deltaPosition { get; private set; }
    public Fix64 deltaRotation { get; private set; }
    public bool playOnAwake { get; set; } = true;
    public string currentStateName => GetCurrentStateName(0);
    /// <summary>The state used as the source while a cross-fade is active.</summary>
    public string? currentSourceStateName => GetPlaybackInfo(0).SourceStateName;
    public int layerCount => ResolveController()?.layerCount ?? 0;
    public string GetCurrentStateName(int layerIndex) => _playbacks.GetValueOrDefault(layerIndex)?.StatePath ?? string.Empty;
    public Fix64 GetNormalizedTime(int layerIndex)
    {
        var playback = _playbacks.GetValueOrDefault(layerIndex);
        var length = MotionLength(ResolveMotions(playback?.State));
        return playback is not null && length > 0 ? playback.StateTime / length : Fix64.Zero;
    }
    public Fix64 GetLayerWeight(int layerIndex) => layerIndex == 0 ? Fix64.One : _layerWeights.GetValueOrDefault(layerIndex,
        ResolveController() is { } controller && layerIndex > 0 && layerIndex < controller.layerCount ? controller.layers[layerIndex - 1].defaultWeight : Fix64.Zero);
    public void SetLayerWeight(int layerIndex, Fix64 weight) { ValidateLayer(layerIndex); if (layerIndex > 0) _layerWeights[layerIndex] = Mathf.Clamp01(weight); }
    public string GetLayerName(int layerIndex) { ValidateLayer(layerIndex); return layerIndex == 0 ? "Base Layer" : ResolveController()!.layers[layerIndex - 1].name; }
    public Fix64 normalizedTime
    {
        get
        {
            var state = _playbacks.GetValueOrDefault(0);
            var length = MotionLength(ResolveMotions(state?.State));
            return state is not null && length > Fix64.Zero ? state.StateTime / length : Fix64.Zero;
        }
    }

    public void Play(string stateName, int layer = -1, Fix64 normalizedTime = default)
    {
        var controller = ResolveController();
        layer = ResolveLayer(stateName, layer);
        if (controller is null || layer < 0) return;
        InLayer(layer, () => PlayCurrentLayer(stateName, normalizedTime));
    }
    private void PlayCurrentLayer(string stateName, Fix64 normalizedTime)
    {
        var controller = ResolveController();
        if (controller is null || _activeLayer >= controller.layerCount) return;
        var route = new AnimatorStateRouter(controller, _activeLayer, Evaluate).Resolve("", stateName, normalizedTime);
        if (route is not null) ApplyRoute(controller, route);
    }
    private void ApplyRoute(AnimatorController controller, AnimatorStateRouter.Route route)
    {
        foreach (var transition in route.Taken) ConsumeTriggers(controller, transition);
        _state = route.State; _playback.StatePath = route.Path;
        _playback.Exited = route.State is null;
        _fromState = null; _playback.FromPath = "";
        _playVersion++;
        _motionCache.Clear();
        _stateTime = MotionLength(ResolveMotions(_state)) * route.Offset;
        _previousTime = _stateTime;
        _eventStartPending = true;
    }
    public void CrossFade(string stateName, Fix64 normalizedTransitionDuration, int layer = -1, Fix64 normalizedTimeOffset = default)
    {
        var controller = ResolveController();
        layer = ResolveLayer(stateName, layer);
        if (controller is null || layer < 0) return;
        InLayer(layer, () => CrossFadeCurrentLayer(stateName, normalizedTransitionDuration, normalizedTimeOffset, false));
    }
    private void CrossFadeCurrentLayer(string stateName, Fix64 duration, Fix64 offset, bool fixedDuration)
    {
        var controller = ResolveController();
        if (controller is null) return;
        var route = new AnimatorStateRouter(controller, _activeLayer, Evaluate).Resolve("", stateName, offset);
        if (route is not null) CrossFadeRoute(controller, route, duration, fixedDuration);
    }
    private void CrossFadeRoute(AnimatorController controller, AnimatorStateRouter.Route route, Fix64 duration, bool fixedDuration)
    {
        var previous = _state;
        var previousPath = _playback.StatePath;
        var previousTime = _stateTime;
        var previousLength = MotionLength(ResolveMotions(previous));
        ApplyRoute(controller, route);
        if (previous is null || previousLength <= 0 || MotionLength(ResolveMotions(_state)) <= 0 || duration <= 0) return;
        _fromState = previous; _playback.FromPath = previousPath;
        _fromTime = previousTime;
        _transitionDuration = fixedDuration ? duration : previousLength * duration /
            Mathf.Max(Fix64.Epsilon, Mathf.Abs(previous.speed));
        _transitionElapsed = Fix64.Zero;
        if (_transitionDuration <= 0) _fromState = null;
    }
    public bool HasState(int layerIndex, string stateName)
    {
        var controller = ResolveController();
        if (controller is null || layerIndex < 0 || layerIndex >= controller.layerCount) return false;
        while (true)
        {
            if (controller.FindState(layerIndex, stateName) is not null) return true;
            if (layerIndex == 0) return false;
            var source = controller.layers[layerIndex - 1].syncedLayerIndex;
            if (source < 0 || source >= layerIndex) return false;
            layerIndex = source;
        }
    }
    public void SetFloat(string name, Fix64 value) => SetBlendParameter(name, value, _floats);
    public Fix64 GetFloat(string name) => _floats.GetValueOrDefault(name);
    public void SetInteger(string name, int value) => SetBlendParameter(name, value, _ints);
    public int GetInteger(string name) => _ints.GetValueOrDefault(name);
    public void SetBool(string name, bool value) { ResolveController(); _bools[name] = value; }
    public bool GetBool(string name) => _bools.GetValueOrDefault(name);
    public void SetTrigger(string name) { ResolveController(); _triggers.Add(name); }
    public void ResetTrigger(string name) => _triggers.Remove(name);
    public bool IsInTransition(int layerIndex) => _playbacks.GetValueOrDefault(layerIndex)?.FromState is not null;

    internal void Tick(Fix64 deltaTime)
    {
        if (!enabled) return;
        deltaPosition = Vector2.zero;
        deltaRotation = Fix64.Zero;
        var controller = ResolveController();
        if (controller is null) return;
        _motionCache.Clear();
        _motionLeaseCursor = 0;
        _motionCacheActive = true;
        _pose.Register(gameObject, controller);
        try
        {
            _pose.RestoreDefaults(applyRootMotion);
            for (var layer = 0; layer < controller.layerCount; layer++)
            {
                var weight = Mathf.Clamp01(GetLayerWeight(layer));
                var settings = layer == 0 ? null : controller.layers[layer - 1];
                if (settings is not null && (!Enum.IsDefined(settings.blendingMode) || settings.syncedLayerIndex < -1))
                    throw new InvalidDataException("Invalid Animator layer settings.");
                AnimatorPose.PoseSnapshot? previous = settings is not null ? _pose.Capture(applyRootMotion) : null;
                AnimatorPose.PoseSnapshot? reference = null;
                if (settings?.blendingMode == AnimatorLayerBlendingMode.Additive)
                {
                    _pose.RestoreDefaults(applyRootMotion);
                    if (settings.referencePose is { isDestroyed: false } referenceClip)
                        SampleReference(referenceClip);
                    reference = _pose.Capture(applyRootMotion, reference: true);
                }
                _pose.BeginLayer();
                if (settings is { syncedLayerIndex: >= 0 })
                {
                    if (settings.syncedLayerIndex >= layer) throw new InvalidDataException("A synchronized layer must follow a preceding layer.");
                    EvaluateLayer(layer, controller, settings.syncedLayerIndex, deltaTime);
                }
                else EvaluateLayer(layer, controller, -1, deltaTime);
                if (previous is not null) _pose.ApplyLayer(previous.Value, weight, settings!, reference);
            }
        }
        finally
        {
            _samples.Clear(); _pose.ReleaseSnapshots();
            _motionCache.Clear(); _motionCacheActive = false;
            _motionLeaseCursor = 0;
        }
    }

    private void SampleSynchronizedLayer(AnimatorController controller, int sourceLayer)
    {
        if (!_playbacks.TryGetValue(sourceLayer, out var source) || source.State is null || source.Exited)
        { _state = _fromState = null; _playback.StatePath = _playback.FromPath = ""; _playback.Exited = true; return; }
        AnimatorState Map(AnimatorState state, string path) => controller.FindState(_activeLayer, path) ?? state;
        Fix64 Time(AnimatorState original, AnimatorState mapped, Fix64 time)
        {
            var sourceLength = MotionLength(ResolveMotions(original));
            return sourceLength > 0 ? time / sourceLength * MotionLength(ResolveMotions(mapped)) : 0;
        }
        _state = Map(source.State, source.StatePath); _playback.StatePath = source.StatePath; _playback.FromPath = source.FromPath; _playback.Exited = false;
        _stateTime = Time(source.State, _state, source.StateTime);
        _previousTime = Time(source.State, _state, source.PreviousTime);
        _fromState = source.FromState is { } from ? Map(from, source.FromPath) : null;
        _fromTime = source.FromState is { } previous ? Time(previous, _fromState!, source.FromTime) : 0;
        _transitionDuration = source.TransitionDuration; _transitionElapsed = source.TransitionElapsed;
        var blend = _fromState is not null && _transitionDuration > 0 ? Mathf.Clamp01(_transitionElapsed / _transitionDuration) : Fix64.One;
        var samples = _samples; samples.Clear();
        if (_fromState is not null) AppendSamples(samples, _fromState, ResolveMotions(_fromState), _fromTime, 1 - blend);
        AppendSamples(samples, _state, ResolveMotions(_state), _stateTime, blend);
        SamplePose();
        // State transitions and events belong to the source machine; a follower only samples its pose.
        _eventStartPending = false;
    }
    private void TickCurrentLayer(AnimatorController controller, Fix64 deltaTime)
    {
        if (_playback.Exited) return;
        if (_state is not null && !ReferenceEquals(controller.FindState(_activeLayer, _playback.StatePath), _state))
        {
            _state = controller.FindState(_activeLayer, _playback.StatePath);
            _fromState = null;
        }
        if (_state is null)
        {
            if (!playOnAwake) return;
            InitializeParameters(controller);
            var entry = new AnimatorStateRouter(controller, _activeLayer, Evaluate).Enter("");
            if (entry is not null) ApplyRoute(controller, entry);
        }
        if (_state is null) return;
        var motions = ResolveMotions(_state);
        var length = MotionLength(motions);
        _previousTime = _stateTime;
        _stateTime += deltaTime * speed * _state.speed;
        var normalized = length > 0 ? _stateTime / length : Fix64.One;
        var context = AnimatorController.ParentPath(_playback.StatePath);
        if (TryTransition(controller, context, normalized, out var route, out var transition))
        {
            var sourcePreviousTime = _previousTime;
            CrossFadeRoute(controller, route!, transition!.duration, transition.hasFixedDuration);
            if (_state is null) return;
            if (_fromState is not null) _fromTime = sourcePreviousTime;
            motions = ResolveMotions(_state);
        }
        if (motions.Length == 0)
        {
            _fromState = null;
            return;
        }
        var rootMotion = WeightedRootMotion(motions, _state!, _previousTime, _stateTime);
        var blend = Fix64.One;
        var fromPrevious = _fromTime;
        var sourceState = _fromState;
        var sourceMotions = ResolveMotions(sourceState);
        if (sourceMotions.Length > 0)
        {
            _fromTime += deltaTime * speed * sourceState!.speed;
            _transitionElapsed += deltaTime * Mathf.Abs(speed);
            blend = Mathf.Clamp01(_transitionElapsed / _transitionDuration);
            var fromMotion = WeightedRootMotion(sourceMotions, sourceState, fromPrevious, _fromTime);
            rootMotion = (Vector2.Lerp(fromMotion.Position, rootMotion.Position, blend),
                Mathf.Lerp(fromMotion.Rotation, rootMotion.Rotation, blend));
            if (blend >= Fix64.One) _fromState = null;
        }
        var samples = _samples; samples.Clear();
        if (sourceMotions.Length > 0 && blend < Fix64.One)
            AppendSamples(samples, sourceState!, sourceMotions, _fromTime, Fix64.One - blend);
        AppendSamples(samples, _state!, motions, _stateTime, blend);
        SamplePose();
        if (_activeLayer == 0) { deltaPosition = rootMotion.Position; deltaRotation = rootMotion.Rotation; }
        if (applyRootMotion && _activeLayer == 0)
        {
            transform.localPosition += deltaPosition;
            transform.localRotation += deltaRotation;
        }
        var includeStart = _eventStartPending;
        _eventStartPending = false;
        var playVersion = _playVersion;
        foreach (var (motion, weight) in sourceMotions)
        {
            if (_playVersion != playVersion) return;
            if (weight > Fix64.Zero)
                DispatchEvents(motion, ScaleMotionTime(sourceMotions, fromPrevious, motion),
                    ScaleMotionTime(sourceMotions, _fromTime, motion),
                    sourceState!.loop || motion.wrapMode == WrapMode.Loop, false, playVersion);
        }
        foreach (var (motion, weight) in motions)
        {
            if (_playVersion != playVersion) return;
            if (weight > Fix64.Zero)
                DispatchEvents(motion, ScaleMotionTime(motions, _previousTime, motion),
                    ScaleMotionTime(motions, _stateTime, motion),
                    _state!.loop || motion.wrapMode == WrapMode.Loop, includeStart, playVersion);
        }
    }

    /// <summary>
    /// Returns a stable snapshot for editor tooling and runtime diagnostics.  The
    /// snapshot is intentionally allocation free and remains valid until the next
    /// animator tick.
    /// </summary>
    public PlaybackInfo GetPlaybackInfo(int layerIndex)
    {
        var playback = _playbacks.GetValueOrDefault(layerIndex);
        var state = playback?.State;
        var motions = ResolveMotions(state);
        var length = MotionLength(motions);
        var normalized = playback is not null && length > Fix64.Zero
            ? playback.StateTime / length : Fix64.Zero;
        var transition = playback is not null && playback.TransitionDuration > Fix64.Zero
            ? Mathf.Clamp01(playback.TransitionElapsed / playback.TransitionDuration) : Fix64.One;
        return new PlaybackInfo(playback?.StatePath ?? string.Empty, playback?.FromState is not null ? playback.FromPath : null,
            normalized, transition, playback?.FromState is not null, motions.Length > 0);
    }

    private MotionSet ResolveMotions(AnimatorState? state)
    {
        if (state is null) return default;
        if (_motionCacheActive && _motionCache.TryGetValue(state, out var cached)) return cached;
        MotionSet result;
        if (state.blendTree is { } tree)
        {
            result = new(tree.Resolve(_readBlendParameter));
            if (_motionCacheActive)
            {
                var source = result;
                var buffer = RentMotionBuffer();
                for (var index = 0; index < source.Length; index++) buffer.Add(source[index]);
                result = new MotionSet(buffer);
            }
        }
        else result = state.ResolveClip() is { } clip ? new(clip) : default;
        if (_motionCacheActive) _motionCache[state] = result;
        return result;
    }

    private List<(AnimationClip Clip, Fix64 Weight)> RentMotionBuffer()
    {
        if (_motionLeaseCursor < _motionBuffers.Count)
        {
            var reused = _motionBuffers[_motionLeaseCursor++];
            reused.Clear();
            return reused;
        }
        var created = new List<(AnimationClip Clip, Fix64 Weight)>(4);
        _motionBuffers.Add(created); _motionLeaseCursor++;
        return created;
    }

    private void SetBlendParameter<T>(string name, T value, Dictionary<string, T> values)
        where T : struct, IEquatable<T>
    {
        ResolveController();
        if (values.TryGetValue(name, out var previous) && previous.Equals(value)) return;
        _parameterChanges.Clear();
        foreach (var playback in _playbacks.Values)
        {
            var stateLength = playback.State?.blendTree?.UsesParameter(name) == true
                ? MotionLength(ResolveMotions(playback.State)) : Fix64.Zero;
            var fromLength = playback.FromState?.blendTree?.UsesParameter(name) == true
                ? MotionLength(ResolveMotions(playback.FromState)) : Fix64.Zero;
            if (stateLength > 0 || fromLength > 0)
                _parameterChanges.Add(new(playback, stateLength, fromLength));
        }
        values[name] = value;
        _motionCache.Clear();
        try
        {
            foreach (var change in _parameterChanges)
            {
                var playback = change.Playback; var stateLength = change.StateLength; var fromLength = change.FromLength;
                if (stateLength > 0 && playback.State is { } state)
                {
                    var nextLength = MotionLength(ResolveMotions(state));
                    if (nextLength > 0 && nextLength != stateLength)
                    {
                        playback.StateTime = playback.StateTime * nextLength / stateLength;
                        playback.PreviousTime = playback.PreviousTime * nextLength / stateLength;
                    }
                }
                if (fromLength > 0 && playback.FromState is { } fromState)
                {
                    var nextLength = MotionLength(ResolveMotions(fromState));
                    if (nextLength > 0 && nextLength != fromLength)
                        playback.FromTime = playback.FromTime * nextLength / fromLength;
                }
            }
        }
        finally { _parameterChanges.Clear(); }
    }

    private Fix64 ReadBlendParameter(string name)
    {
        if (_effectiveController is not { } controller) return Fix64.Zero;
        foreach (var parameter in controller.parameters)
        {
            if (parameter.name != name) continue;
            return parameter.type switch
            {
                AnimatorControllerParameterType.Float => _floats.GetValueOrDefault(name),
                AnimatorControllerParameterType.Int => (Fix64)_ints.GetValueOrDefault(name),
                _ => Fix64.Zero
            };
        }
        return Fix64.Zero;
    }

    private readonly record struct ParameterPlaybackLength(Playback Playback, Fix64 StateLength, Fix64 FromLength);

    private static Fix64 MotionLength(MotionSet motions)
    {
        var length = Fix64.Zero;
        foreach (var (clip, weight) in motions) length += clip.length * weight;
        return length;
    }

    private static void AppendSamples(List<(AnimationClip Clip, Fix64 Time, Fix64 Weight)> samples,
        AnimatorState state, MotionSet motions,
        Fix64 time, Fix64 stateWeight)
    {
        var length = MotionLength(motions);
        foreach (var (clip, weight) in motions)
            if (weight > Fix64.Zero)
                samples.Add((clip, Wrap(ScaleMotionTime(length, time, clip), clip.length,
                        state.loop || clip.wrapMode == WrapMode.Loop),
                    weight * stateWeight));
    }

    private static Fix64 ScaleMotionTime(MotionSet motions,
        Fix64 stateTime, AnimationClip clip)
    {
        var length = MotionLength(motions);
        return ScaleMotionTime(length, stateTime, clip);
    }

    private static Fix64 ScaleMotionTime(Fix64 length, Fix64 stateTime, AnimationClip clip) =>
        length > Fix64.Zero ? stateTime * clip.length / length : Fix64.Zero;

    private static (Vector2 Position, Fix64 Rotation) WeightedRootMotion(
        MotionSet motions, AnimatorState state,
        Fix64 previous, Fix64 current)
    {
        var position = Vector2.zero;
        var rotation = Fix64.Zero;
        var length = MotionLength(motions);
        foreach (var (clip, weight) in motions)
        {
            var motion = AnimationSampler.RootMotion(clip,
                ScaleMotionTime(length, previous, clip), ScaleMotionTime(length, current, clip),
                state.loop || clip.wrapMode == WrapMode.Loop);
            position += motion.Position * weight;
            rotation += motion.Rotation * weight;
        }
        return (position, rotation);
    }

    private AnimatorController? ResolveController()
    {
        var controller = runtimeAnimatorController is { isDestroyed: false } live ? live : null;
        if (!ReferenceEquals(controller, _effectiveController))
        {
            if (_effectiveController is not null) { _floats.Clear(); _ints.Clear(); _bools.Clear(); _triggers.Clear(); }
            _effectiveController = controller;
            _playbacks.Clear();
            _layerWeights.Clear();
            _motionCache.Clear();
            _motionBuffers.Clear(); _motionLeaseCursor = 0;
            _pose.Clear();
            _playback = new Playback();
            if (controller is not null) { controller.ValidateHierarchy(); InitializeParameters(controller); }
        }
        return controller;
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

    private void ConsumeTriggers(AnimatorController controller, AnimatorTransition transition)
    {
        foreach (var condition in transition.conditions)
            if (controller.parameters.Any(parameter => parameter.name == condition.parameter && parameter.type == AnimatorControllerParameterType.Trigger))
                _triggers.Remove(condition.parameter);
    }

    private bool Evaluate(AnimatorCondition condition)
    {
        if (_effectiveController?.parameters.FirstOrDefault(parameter => parameter.name == condition.parameter)?.type == AnimatorControllerParameterType.Trigger)
            return condition.mode == AnimatorConditionMode.IfNot ? !_triggers.Contains(condition.parameter) : _triggers.Contains(condition.parameter);
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

    private void DispatchEvents(AnimationClip clip, Fix64 previous, Fix64 current, bool loop,
        bool includeStart, int playVersion)
    {
        if (clip.events.Count == 0) return;
        var reverse = current < previous;
        var events = reverse
            ? clip.events.OrderBy(static item => -item.time).ToArray()
            : clip.events.OrderBy(static item => item.time).ToArray();
        var firstCycle = loop && clip.length > 0 ? Mathf.FloorToInt(previous / clip.length) : 0;
        var lastCycle = loop && clip.length > 0 ? Mathf.FloorToInt(current / clip.length) : 0;
        for (var cycle = firstCycle; ; cycle += reverse ? -1 : 1)
        {
            foreach (var animationEvent in events)
            {
                var eventTime = animationEvent.time + cycle * clip.length;
                var inRange = reverse
                    ? eventTime < previous && eventTime >= current
                    : (eventTime > previous || includeStart && eventTime == previous) && eventTime <= current;
                if (!inRange) continue;
                foreach (var component in gameObject.components.ToArray())
                {
                    if (_playVersion != playVersion) return;
                    if (component is not MonoBehaviour { enabled: true } behaviour) continue;
                    try
                    {
                        if (!RuntimeTypeCache.TryInvokeMessage(behaviour, animationEvent.functionName, animationEvent))
                            RuntimeTypeCache.TryInvokeMessage(behaviour, animationEvent.functionName);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"{behaviour.GetType().FullName}.{animationEvent.functionName} failed: " +
                                       exception.Message);
                    }
                }
            }
            if (cycle == lastCycle) break;
        }
    }

    private static Fix64 Wrap(Fix64 time, Fix64 length, bool loop) =>
        length <= 0 ? Fix64.Zero : loop ? Mathf.Repeat(time, length) : Mathf.Clamp(time, Fix64.Zero, length);

    private int ResolveLayer(string stateName, int layer)
    {
        var controller = ResolveController();
        if (controller is null) return -1;
        if (layer >= 0) { ValidateLayer(layer); return layer; }
        if (layer != -1) throw new ArgumentOutOfRangeException(nameof(layer));
        for (var index = 0; index < controller.layerCount; index++)
            if (controller.ResolveDestinationPath(index, "", stateName) is not null) return index;
        return -1;
    }
    private void InLayer(int layer, Action action)
    {
        var previous = _playback; var previousLayer = _activeLayer; var previousWeight = _layerWeight;
        if (!_playbacks.TryGetValue(layer, out var playback)) _playbacks[layer] = playback = new Playback();
        _playback = playback; _activeLayer = layer; _layerWeight = GetLayerWeight(layer);
        try { action(); }
        finally { _playback = previous; _activeLayer = previousLayer; _layerWeight = previousWeight; }
    }
    private void ValidateLayer(int layer)
    {
        if (layer < 0 || layer >= (ResolveController()?.layerCount ?? 0))
            throw new ArgumentOutOfRangeException(nameof(layer));
    }
}
