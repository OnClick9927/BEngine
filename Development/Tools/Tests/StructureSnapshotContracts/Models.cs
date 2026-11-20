using BEngine;

public sealed class StructureValue : MonoBehaviour
{
    public int Count;
    public BObject? Borrowed;
}

public sealed class StructureRequired : MonoBehaviour { }

[RequireComponent(typeof(StructureRequired))]
public sealed class StructureDependent : MonoBehaviour { }

public sealed class StructureAsset : BAsset { }
