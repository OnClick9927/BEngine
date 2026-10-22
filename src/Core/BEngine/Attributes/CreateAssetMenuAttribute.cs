namespace BEngine;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CreateAssetMenuAttribute : Attribute
{
    public string fileName { get; set; } = "New Scriptable Object";
    public string menuName { get; set; } = "";
    public int order { get; set; }
}
