namespace BEngine.Cinemachine;

public readonly record struct CinemachineBlend(
    CinemachineCamera? source,
    CinemachineCamera destination,
    CinemachineBlendDefinition definition,
    Fix64 elapsed)
{
    public CinemachineCamera? Source => source;
    public CinemachineCamera Destination => destination;
    public CinemachineBlendDefinition Definition => definition;
    public Fix64 Elapsed => elapsed;
    public bool IsComplete => isComplete;
    public Fix64 Weight => weight;
    public bool isComplete => definition.Evaluate(elapsed) >= Fix64.One;
    public Fix64 weight => definition.Evaluate(elapsed);
}
