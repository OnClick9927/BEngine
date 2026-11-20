using BEngine;

public sealed class IdentityAsset : ScriptableObject, ISerializationCallbackReceiver
{
    public int Count;
    public IdentityNode? First, Second;
    [SerializationIgnore] public int Restored;
    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize() => Restored++;
}
public sealed class IdentityNode { public int Value; public IdentityNode? Next; }
public sealed class IdentityObject : BObject { public int Count; }
