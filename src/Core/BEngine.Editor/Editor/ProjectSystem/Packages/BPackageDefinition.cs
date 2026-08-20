using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

public sealed record BPackageDefinition(string Path, PackageDefinitionDocument Document);
