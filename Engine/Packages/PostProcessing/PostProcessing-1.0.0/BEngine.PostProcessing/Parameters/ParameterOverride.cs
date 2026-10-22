namespace BEngine.PostProcessing;

public class ParameterOverride<T> : ParameterOverrideBase
{
    public T value { get; set; }

    public ParameterOverride() => value = default!;
    public ParameterOverride(T value, bool overrideState = false)
    {
        this.value = value;
        this.overrideState = overrideState;
    }
}
