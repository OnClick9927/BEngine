using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class PackageContentDocument
{
    public string Runtime { get; set; } = "Resources";
    public string Editor { get; set; } = "Editor";
}
