namespace BEngine;

internal static class SerializationCallbackUtility
{
    private static int _beforeSerializeSuppression;
    private static int _afterDeserializeSuppression;

    internal static void BeforeSerialize(object value)
    {
        if (_beforeSerializeSuppression > 0) return;
        if (value is ISerializationCallbackReceiver receiver)
        {
            try { receiver.OnBeforeSerialize(); }
            catch (Exception exception)
            {
                LogFailure(value, nameof(ISerializationCallbackReceiver.OnBeforeSerialize), exception);
            }
        }
    }

    internal static void AfterDeserialize(object value, bool validateInEditor = true)
    {
        if (_afterDeserializeSuppression > 0) return;
        if (value is ISerializationCallbackReceiver receiver)
        {
            try { receiver.OnAfterDeserialize(); }
            catch (Exception exception)
            {
                LogFailure(value, nameof(ISerializationCallbackReceiver.OnAfterDeserialize), exception);
            }
        }
        if (validateInEditor && Application.isEditor && !Application.isPlaying &&
            value is MonoBehaviour behaviour)
        {
            try { behaviour.OnValidate(); }
            catch (Exception exception)
            {
                LogFailure(value, nameof(MonoBehaviour.OnValidate), exception);
            }
        }
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

    private static void LogFailure(object target, string callbackName, Exception exception) =>
        Debug.LogError($"{target.GetType().FullName}.{callbackName} failed: {exception.Message}");

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
