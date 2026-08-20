namespace BEngine;

internal static class SerializationCallbackUtility
{
    internal static void BeforeSerialize(object value)
    {
        if (value is ISerializationCallbackReceiver receiver)
            Invoke(receiver.OnBeforeSerialize, value, nameof(ISerializationCallbackReceiver.OnBeforeSerialize));
    }

    internal static void AfterDeserialize(object value)
    {
        if (value is ISerializationCallbackReceiver receiver)
            Invoke(receiver.OnAfterDeserialize, value, nameof(ISerializationCallbackReceiver.OnAfterDeserialize));
        if (EngineThreadContext.IsMainThread && Application.isEditor && !Application.isPlaying &&
            value is MonoBehaviour behaviour)
            Invoke(behaviour.OnValidate, value, nameof(MonoBehaviour.OnValidate));
    }

    private static void Invoke(Action callback, object target, string callbackName)
    {
        try { callback(); }
        catch (Exception exception)
        {
            Debug.LogError($"{target.GetType().FullName}.{callbackName} failed: {exception.Message}");
        }
    }
}
