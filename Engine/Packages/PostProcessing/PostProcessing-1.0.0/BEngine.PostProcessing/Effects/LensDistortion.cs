namespace BEngine.PostProcessing;

/// <summary>Barrel or pincushion distortion around the frame center.</summary>
public sealed class LensDistortion : PostProcessEffectSettings
{
    public Fix64Parameter intensity { get; set; } = new(Fix64.Zero, true);
    public Fix64Parameter xMultiplier { get; set; } = new(Fix64.One, true);
    public Fix64Parameter yMultiplier { get; set; } = new(Fix64.One, true);
    public override bool IsEnabledAndSupported() => active && intensity.value != Fix64.Zero;
}
