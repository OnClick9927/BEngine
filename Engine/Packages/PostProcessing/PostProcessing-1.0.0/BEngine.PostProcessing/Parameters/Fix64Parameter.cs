namespace BEngine.PostProcessing;

public sealed class Fix64Parameter : ParameterOverride<Fix64>
{
    public Fix64Parameter() { }

    public Fix64Parameter(Fix64 value, bool overrideState = false)
        : base(value, overrideState) { }
}
