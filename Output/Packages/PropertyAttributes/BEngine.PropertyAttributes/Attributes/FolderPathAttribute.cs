namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class FolderPathAttribute(string description = "Select Folder", bool relativeToProject = true)
    : ExtendedPropertyAttribute
{
    public string description { get; } = description;
    public bool relativeToProject { get; } = relativeToProject;
}
