using BEngine.Serialization;

namespace BEngine;

internal static class AnimationClipSerialization
{
    internal static AnimationClip Restore(AnimationClipData document)
    {
        Validate(document);
        var clip = new AnimationClip
        {
            name = document.Name,
            frameRate = Fix64.FromRaw(document.FrameRate),
            wrapMode = document.WrapMode,
            legacy = document.Legacy,
            bindings = [.. document.Bindings.Select(binding => new AnimationBinding
            {
                relativePath = binding.RelativePath,
                componentType = binding.ComponentType,
                propertyName = binding.PropertyName,
                curve = new AnimationCurve([.. binding.Keys.Select(key => new Keyframe(
                    Fix64.FromRaw(key.Time), Fix64.FromRaw(key.Value), Fix64.FromRaw(key.InTangent),
                    Fix64.FromRaw(key.OutTangent)))])
                {
                    preWrapMode = binding.PreWrapMode,
                    postWrapMode = binding.PostWrapMode
                }
            })],
            events = [.. document.Events.Select(item => new AnimationEvent
            {
                time = Fix64.FromRaw(item.Time),
                functionName = item.FunctionName,
                stringParameter = item.StringParameter,
                intParameter = item.IntParameter,
                floatParameter = Fix64.FromRaw(item.FloatParameter)
            })]
        };
        if (document.Version == 2)
        {
            if (document.BindingGraph is null) throw new InvalidDataException("Missing animation binding graph.");
            ComponentObjectGraphSerializer.Restore(clip, document.BindingGraph);
            foreach (var binding in clip.bindings)
                if (binding.kind == AnimationBindingKind.ObjectReference)
                    foreach (var key in binding.curve.keys) binding.ObjectAt(key.value);
        }
        return clip;
    }

    internal static AnimationClipData Capture(AnimationClip clip) => new()
    {
        Name = clip.name,
        FrameRate = clip.frameRate.RawValue,
        WrapMode = clip.wrapMode,
        Legacy = clip.legacy,
        BindingGraph = ComponentObjectGraphSerializer.Capture(clip,
            [typeof(AnimationClip).GetProperty(nameof(AnimationClip.bindings))!]),
        Events = [.. clip.events.Select(item => new AnimationEventData
        {
            Time = item.time.RawValue,
            FunctionName = item.functionName,
            StringParameter = item.stringParameter,
            IntParameter = item.intParameter,
            FloatParameter = item.floatParameter.RawValue
        })]
    };

    internal static void Validate(AnimationClipData document)
    {
        if (document.Format != "BEngine.AnimationClip" || document.Version is not (1 or 2))
            throw new InvalidDataException("Unsupported animation clip.");
    }

    internal static AnimationClip Load(string path) => Document<AnimationClip>
        .Read(path, sourcePath => Restore(YamlUtility.Load<AnimationClipData>(sourcePath))).ToAsset();

    internal static void Save(AnimationClip clip, string path) => Document<AnimationClip>.FromAsset(clip)
        .Write(path, static (asset, destination) => YamlUtility.Save(Capture(asset), destination));
}
