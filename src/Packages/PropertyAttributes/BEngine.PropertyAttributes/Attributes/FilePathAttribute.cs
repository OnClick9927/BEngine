namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class FilePathAttribute(string title = "Select File", string filter = "All files (*.*)|*.*",
    bool relativeToProject = true) : ExtendedPropertyAttribute
{
    public string title { get; } = title;
    public string filter { get; } = filter;
    public bool relativeToProject { get; } = relativeToProject;
}
