namespace BEngine;

internal static class SerializationCallbackUtility
{
    [ThreadStatic]
    private static int _beforeSerializeSuppression;
    [ThreadStatic]
    private static int _afterDeserializeSuppression;

    internal static void BeforeSerialize(object value)
    {
        if (_beforeSerializeSuppression > 0) return;
        if (value is ISerializationCallbackReceiver receiver)
            Invoke(receiver.OnBeforeSerialize, value, nameof(ISerializationCallbackReceiver.OnBeforeSerialize));
    }

    internal static void AfterDeserialize(object value, bool validateInEditor = true)
    {
        if (_afterDeserializeSuppression > 0) return;
        if (value is ISerializationCallbackReceiver receiver)
            Invoke(receiver.OnAfterDeserialize, value, nameof(ISerializationCallbackReceiver.OnAfterDeserialize));
        if (validateInEditor && Application.isEditor && !Application.isPlaying &&
            value is MonoBehaviour behaviour)
            Invoke(behaviour.OnValidate, value, nameof(MonoBehaviour.OnValidate));
    }

    internal static IDisposable SuppressAfterDeserialize()
    {
        _afterDeserializeSuppression++;
        return new CallbackSuppressionScope(
            static () => _afterDeserializeSuppression = Math.Max(0, _afterDeserializeSuppression - 1));
    }

    internal static IDisposable SuppressBeforeSerialize()
    {
        _beforeSerializeSuppression++;
        return new CallbackSuppressionScope(
            static () => _beforeSerializeSuppression = Math.Max(0, _beforeSerializeSuppression - 1));
    }

    private static void Invoke(Action callback, object target, string callbackName)
    {
        try { callback(); }
        catch (Exception exception)
        {
            Debug.LogError($"{target.GetType().FullName}.{callbackName} failed: {exception.Message}");
        }
    }

    private sealed class CallbackSuppressionScope(Action release) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            release();
        }
    }
}
