using BEngine.Editor.Documents;

namespace BEngine.Editor;

public sealed class AssemblyDefinitionAsset : DefaultAsset
{
    public AssemblyDefinitionDocument definition { get; internal set; } = new();
    public string importError { get; internal set; } = string.Empty;
}
