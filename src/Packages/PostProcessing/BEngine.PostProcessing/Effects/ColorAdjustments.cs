namespace BEngine.PostProcessing;

public sealed class ColorAdjustments : PostProcessEffectSettings
{
    public Fix64Parameter postExposure { get; set; } = new(Fix64.Zero, true);
    public Fix64Parameter contrast { get; set; } = new(Fix64.Zero, true);
    public Fix64Parameter hueShift { get; set; } = new(Fix64.Zero, true);
    public Fix64Parameter saturation { get; set; } = new(Fix64.Zero, true);
    public ColorParameter colorFilter { get; set; } = new(Color.white, true);

    public override bool IsEnabledAndSupported() => active &&
        (postExposure.value != Fix64.Zero || contrast.value != Fix64.Zero ||
         hueShift.value != Fix64.Zero || saturation.value != Fix64.Zero ||
         !colorFilter.value.Equals(Color.white));
}
