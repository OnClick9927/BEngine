using BEngine.Documents;

namespace BEngine.Animation;

internal sealed class AnimationClipDocumentConverter : DocumentConverter<AnimationClipDocument, AnimationClip>
{
    protected override AnimationClip ToBObject(AnimationClipDocument document, DocumentConversionContext context)
    {
        Validate(document);
        return new AnimationClip
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
    }

    protected override AnimationClipDocument FromBObject(AnimationClip clip, DocumentConversionContext context) => new()
    {
        Name = clip.name,
        FrameRate = clip.frameRate.RawValue,
        WrapMode = clip.wrapMode,
        Legacy = clip.legacy,
        Bindings = [.. clip.bindings.Select(binding => new AnimationBindingDocument
        {
            RelativePath = binding.relativePath,
            ComponentType = binding.componentType,
            PropertyName = binding.propertyName,
            PreWrapMode = binding.curve.preWrapMode,
            PostWrapMode = binding.curve.postWrapMode,
            Keys = [.. binding.curve.keys.Select(key => new KeyframeDocument
            {
                Time = key.time.RawValue,
                Value = key.value.RawValue,
                InTangent = key.inTangent.RawValue,
                OutTangent = key.outTangent.RawValue
            })]
        })],
        Events = [.. clip.events.Select(item => new AnimationEventDocument
        {
            Time = item.time.RawValue,
            FunctionName = item.functionName,
            StringParameter = item.stringParameter,
            IntParameter = item.intParameter,
            FloatParameter = item.floatParameter.RawValue
        })]
    };

    internal static void Validate(AnimationClipDocument document)
    {
        if (document.Format != "BEngine.AnimationClip" || document.Version != 1)
            throw new InvalidDataException("Unsupported animation clip.");
    }
}
