using BEngine.Serialization;

namespace BEngine.Animation;

internal static class AnimationAssetSerializer
{
    public static AnimationClipDocument ToDocument(AnimationClip clip) => new()
    {
        Name = clip.name, FrameRate = clip.frameRate.RawValue, WrapMode = clip.wrapMode, Legacy = clip.legacy,
        Bindings = [.. clip.bindings.Select(binding => new AnimationBindingDocument
        {
            RelativePath = binding.relativePath, ComponentType = binding.componentType, PropertyName = binding.propertyName,
            PreWrapMode = binding.curve.preWrapMode, PostWrapMode = binding.curve.postWrapMode,
            Keys = [.. binding.curve.keys.Select(key => new KeyframeDocument
            { Time = key.time.RawValue, Value = key.value.RawValue, InTangent = key.inTangent.RawValue, OutTangent = key.outTangent.RawValue })]
        })],
        Events = [.. clip.events.Select(item => new AnimationEventDocument
        { Time = item.time.RawValue, FunctionName = item.functionName, StringParameter = item.stringParameter,
            IntParameter = item.intParameter, FloatParameter = item.floatParameter.RawValue })]
    };

    public static AnimationClip LoadClip(string path)
    {
        var document = YamlUtility.Load<AnimationClipDocument>(path);
        if (document.Format != "BEngine.AnimationClip" || document.Version != 1) throw new InvalidDataException("Unsupported animation clip.");
        var clip = new AnimationClip { name = document.Name, frameRate = Fix64.FromRaw(document.FrameRate), wrapMode = document.WrapMode, legacy = document.Legacy };
        clip.bindings = [.. document.Bindings.Select(binding => new AnimationBinding
        {
            relativePath = binding.RelativePath, componentType = binding.ComponentType, propertyName = binding.PropertyName,
            curve = new AnimationCurve([.. binding.Keys.Select(key => new Keyframe(Fix64.FromRaw(key.Time), Fix64.FromRaw(key.Value), Fix64.FromRaw(key.InTangent), Fix64.FromRaw(key.OutTangent)))])
            { preWrapMode = binding.PreWrapMode, postWrapMode = binding.PostWrapMode }
        })];
        clip.events = [.. document.Events.Select(item => new AnimationEvent { time = Fix64.FromRaw(item.Time), functionName = item.FunctionName,
            stringParameter = item.StringParameter, intParameter = item.IntParameter, floatParameter = Fix64.FromRaw(item.FloatParameter) })];
        return clip;
    }

    public static AnimatorControllerDocument ToDocument(AnimatorController controller) => new()
    {
        Name = controller.name, DefaultState = controller.defaultState,
        Parameters = [.. controller.parameters.Select(item => new AnimatorParameterDocument { Name = item.name, Type = item.type,
            DefaultFloat = item.defaultFloat.RawValue, DefaultInt = item.defaultInt, DefaultBool = item.defaultBool })],
        States = [.. controller.states.Select(state => new AnimatorStateDocument { Name = state.name, ClipPath = state.clipPath,
            Speed = state.speed.RawValue, Loop = state.loop, Transitions = [.. state.transitions.Select(transition => new AnimatorTransitionDocument
            { DestinationState = transition.destinationState, HasExitTime = transition.hasExitTime, ExitTime = transition.exitTime.RawValue,
                Duration = transition.duration.RawValue, Conditions = [.. transition.conditions.Select(condition => new AnimatorConditionDocument
                { Parameter = condition.parameter, Mode = condition.mode, Threshold = condition.threshold.RawValue })] })] })]
    };

    public static AnimatorController LoadController(string path)
    {
        var document = YamlUtility.Load<AnimatorControllerDocument>(path);
        if (document.Format != "BEngine.AnimatorController" || document.Version != 1) throw new InvalidDataException("Unsupported animator controller.");
        var controller = new AnimatorController { name = document.Name, defaultState = document.DefaultState };
        controller.parameters = [.. document.Parameters.Select(item => new AnimatorControllerParameter { name = item.Name, type = item.Type,
            defaultFloat = Fix64.FromRaw(item.DefaultFloat), defaultInt = item.DefaultInt, defaultBool = item.DefaultBool })];
        controller.states = [.. document.States.Select(state => new AnimatorState { name = state.Name, clipPath = state.ClipPath,
            speed = Fix64.FromRaw(state.Speed), loop = state.Loop, transitions = [.. state.Transitions.Select(transition => new AnimatorTransition
            { destinationState = transition.DestinationState, hasExitTime = transition.HasExitTime,
                exitTime = Fix64.FromRaw(transition.ExitTime), duration = Fix64.FromRaw(transition.Duration),
                conditions = [.. transition.Conditions.Select(condition => new AnimatorCondition { parameter = condition.Parameter,
                    mode = condition.Mode, threshold = Fix64.FromRaw(condition.Threshold) })] })] })];
        return controller;
    }
}

public sealed class AnimationClipDocument
{
    public string Format { get; set; } = "BEngine.AnimationClip"; public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Animation"; public long FrameRate { get; set; } = 60L << Fix64.FractionalBits;
    public WrapMode WrapMode { get; set; } public bool Legacy { get; set; }
    public List<AnimationBindingDocument> Bindings { get; set; } = []; public List<AnimationEventDocument> Events { get; set; } = [];
}
public sealed class AnimationBindingDocument
{
    public string RelativePath { get; set; } = string.Empty; public string ComponentType { get; set; } = typeof(Transform).FullName!;
    public string PropertyName { get; set; } = string.Empty; public WrapMode PreWrapMode { get; set; } = WrapMode.ClampForever;
    public WrapMode PostWrapMode { get; set; } = WrapMode.ClampForever; public List<KeyframeDocument> Keys { get; set; } = [];
}
public sealed class KeyframeDocument { public long Time { get; set; } public long Value { get; set; } public long InTangent { get; set; } public long OutTangent { get; set; } }
public sealed class AnimationEventDocument { public long Time { get; set; } public string FunctionName { get; set; } = string.Empty; public string StringParameter { get; set; } = string.Empty; public int IntParameter { get; set; } public long FloatParameter { get; set; } }
public sealed class AnimatorControllerDocument
{
    public string Format { get; set; } = "BEngine.AnimatorController"; public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Animator Controller"; public string DefaultState { get; set; } = string.Empty;
    public List<AnimatorParameterDocument> Parameters { get; set; } = []; public List<AnimatorStateDocument> States { get; set; } = [];
}
public sealed class AnimatorParameterDocument { public string Name { get; set; } = string.Empty; public AnimatorControllerParameterType Type { get; set; } public long DefaultFloat { get; set; } public int DefaultInt { get; set; } public bool DefaultBool { get; set; } }
public sealed class AnimatorStateDocument { public string Name { get; set; } = string.Empty; public string ClipPath { get; set; } = string.Empty; public long Speed { get; set; } = Fix64.OneRaw; public bool Loop { get; set; } = true; public List<AnimatorTransitionDocument> Transitions { get; set; } = []; }
public sealed class AnimatorTransitionDocument { public string DestinationState { get; set; } = string.Empty; public bool HasExitTime { get; set; } public long ExitTime { get; set; } = Fix64.OneRaw; public long Duration { get; set; } public List<AnimatorConditionDocument> Conditions { get; set; } = []; }
public sealed class AnimatorConditionDocument { public string Parameter { get; set; } = string.Empty; public AnimatorConditionMode Mode { get; set; } public long Threshold { get; set; } }
