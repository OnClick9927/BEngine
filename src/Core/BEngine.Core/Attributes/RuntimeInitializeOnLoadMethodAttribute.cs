namespace BEngine;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RuntimeInitializeOnLoadMethodAttribute(
    RuntimeInitializeLoadType loadType = RuntimeInitializeLoadType.AfterSceneLoad) : Attribute
{
    public RuntimeInitializeLoadType loadType { get; } = loadType;
}
