namespace BEngine.PostProcessing;

internal readonly record struct PostProcessStack2D(
    Fix64 Exposure,
    Fix64 Contrast,
    Fix64 HueShift,
    Fix64 Saturation,
    Color ColorFilter,
    Fix64 BloomIntensity,
    Fix64 BloomThreshold,
    Fix64 BloomScatter,
    Color VignetteColor,
    Fix64 VignetteIntensity,
    Fix64 VignetteSmoothness,
    Fix64 VignetteRoundness,
    Fix64 ChromaticAberration,
    Fix64 FilmGrain,
    Fix64 LensDistortion,
    Fix64 ColorLookupContribution)
{
    private static readonly Fix64 DefaultBloomScatter = Fix64.Parse("0.7");
    private static readonly Fix64 DefaultVignetteSmoothness = Fix64.Parse("0.2");

    internal bool IsActive => Exposure != Fix64.Zero || Contrast != Fix64.Zero ||
        HueShift != Fix64.Zero || Saturation != Fix64.Zero || !ColorFilter.Equals(Color.white) ||
        BloomIntensity > Fix64.Zero || VignetteIntensity > Fix64.Zero ||
        ChromaticAberration > Fix64.Zero || FilmGrain > Fix64.Zero ||
        LensDistortion != Fix64.Zero || ColorLookupContribution > Fix64.Zero;

    internal static PostProcessStack2D Resolve(Scene scene, PostProcessLayer layer)
    {
        var trigger = layer.volumeTrigger?.position ?? layer.transform.position;
        var result = new PostProcessStack2D(
            Fix64.Zero, Fix64.Zero, Fix64.Zero, Fix64.Zero, Color.white,
            Fix64.Zero, Fix64.One, DefaultBloomScatter, Color.black,
            Fix64.Zero, DefaultVignetteSmoothness, Fix64.One,
            Fix64.Zero, Fix64.Zero, Fix64.Zero, Fix64.Zero);
        // QueryComponents is already a retained scene snapshot. Resolve the
        // few active volumes in priority order without LINQ iterator/array
        // allocations; this path runs once per post-processed camera frame.
        var volumes = scene.QueryComponents<PostProcessVolume>();
        var hasPrevious = false;
        var previousPriority = Fix64.FromRaw(long.MinValue);
        var previousId = int.MinValue;
        while (true)
        {
            PostProcessVolume? next = null;
            var nextPriority = Fix64.FromRaw(long.MaxValue);
            var nextId = int.MaxValue;
            for (var index = 0; index < volumes.Count; index++)
            {
                var candidate = volumes[index];
                if (!layer.volumeLayer.Contains(candidate.gameObject.layer)) continue;
                var candidateId = candidate.GetInstanceID();
                if (hasPrevious && (candidate.priority < previousPriority ||
                    candidate.priority == previousPriority && candidateId <= previousId)) continue;
                if (candidate.priority < nextPriority ||
                    candidate.priority == nextPriority && candidateId < nextId)
                {
                    next = candidate; nextPriority = candidate.priority; nextId = candidateId;
                }
            }
            if (next is null) break;
            previousPriority = nextPriority; previousId = nextId; hasPrevious = true;
            var influence = next.GetInfluence(trigger);
            if (influence <= Fix64.Zero || next.profile is not { } profile) continue;
            result = Blend(result, profile, influence);
        }
        return result;
    }

    private static PostProcessStack2D Blend(
        PostProcessStack2D current,
        PostProcessProfile profile,
        Fix64 weight)
    {
        var color = profile.colorAdjustments;
        var bloom = profile.bloom;
        var vignette = profile.vignette;
        return new PostProcessStack2D(
            Blend(current.Exposure, color.postExposure, color.active, weight),
            Blend(current.Contrast, color.contrast, color.active, weight),
            Blend(current.HueShift, color.hueShift, color.active, weight),
            Blend(current.Saturation, color.saturation, color.active, weight),
            Blend(current.ColorFilter, color.colorFilter, color.active, weight),
            Blend(current.BloomIntensity, bloom.intensity, bloom.active, weight),
            Blend(current.BloomThreshold, bloom.threshold, bloom.active, weight),
            Blend(current.BloomScatter, bloom.scatter, bloom.active, weight),
            Blend(current.VignetteColor, vignette.color, vignette.active, weight),
            Blend(current.VignetteIntensity, vignette.intensity, vignette.active, weight),
            Blend(current.VignetteSmoothness, vignette.smoothness, vignette.active, weight),
            Blend(current.VignetteRoundness, vignette.roundness, vignette.active, weight),
            Blend(current.ChromaticAberration, profile.chromaticAberration.intensity,
                profile.chromaticAberration.active, weight),
            Blend(current.FilmGrain, profile.filmGrain.intensity,
                profile.filmGrain.active, weight),
            Blend(current.LensDistortion, profile.lensDistortion.intensity,
                profile.lensDistortion.active, weight),
            Blend(current.ColorLookupContribution, profile.colorLookup.contribution,
                profile.colorLookup.active && profile.colorLookup.texture is { isDestroyed: false }, weight));
    }

    private static Fix64 Blend(Fix64 current, Fix64Parameter parameter, bool active, Fix64 weight) =>
        active && parameter.overrideState ? Mathf.Lerp(current, parameter.value, weight) : current;

    private static Color Blend(Color current, ColorParameter parameter, bool active, Fix64 weight) =>
        active && parameter.overrideState
            ? new Color(
                Mathf.Lerp(current.r, parameter.value.r, weight),
                Mathf.Lerp(current.g, parameter.value.g, weight),
                Mathf.Lerp(current.b, parameter.value.b, weight),
                Mathf.Lerp(current.a, parameter.value.a, weight))
            : current;
}
