using BEngine.Serialization;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem.Editor;

public sealed record ProjectAssemblyDefinition(string Path, AssemblyDefinitionDocument Document);
