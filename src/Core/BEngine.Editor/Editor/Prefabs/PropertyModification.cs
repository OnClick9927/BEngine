using BEngine.Serialization;

namespace BEngine.Editor;

public readonly record struct PropertyModification(BObject Target, string PropertyPath, string Value);
