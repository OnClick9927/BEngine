namespace BEngine;

public interface ISerializationCallbackReceiver
{
    void OnBeforeSerialize();
    void OnAfterDeserialize();
}
