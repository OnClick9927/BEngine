namespace BEngine.Cinemachine;

public readonly record struct CinemachineBlendDefinition
{
    public CinemachineBlendStyle style { get; init; }
    public Fix64 time { get; init; }

    public CinemachineBlendDefinition(CinemachineBlendStyle style, Fix64 time)
    {
        this.style = style;
        this.time = Fix64.Max(Fix64.Zero, time);
    }

    public Fix64 Evaluate(Fix64 elapsed)
    {
        if (style == CinemachineBlendStyle.Cut || time <= Fix64.Zero) return Fix64.One;
        var value = Mathf.Clamp01(elapsed / time);
        return style switch
        {
            CinemachineBlendStyle.EaseInOut => value * value * (3 - 2 * value),
            CinemachineBlendStyle.EaseIn => value * value,
            CinemachineBlendStyle.EaseOut => Fix64.One - (Fix64.One - value) * (Fix64.One - value),
            _ => value
        };
    }
}
