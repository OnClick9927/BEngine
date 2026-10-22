namespace BEngine;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HelpURLAttribute(string url) : Attribute
{
    public string URL { get; } = url;
}
