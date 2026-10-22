namespace BEngine.PostProcessing;

public sealed class ColorParameter : ParameterOverride<Color>
{
    public ColorParameter() : base(Color.white) { }

    public ColorParameter(Color value, bool overrideState = false)
        : base(value, overrideState) { }
}
