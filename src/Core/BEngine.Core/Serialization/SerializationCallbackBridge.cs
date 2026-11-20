namespace BEngine;

internal static class SerializationCallbackBridge
{
    private static Action<object>? _afterDeserialize;
    private static Func<Type, bool>? _isEngineObject;
    private static Func<string, Type, object>? _deserializeScalar;

    internal static void RegisterAfterDeserialize(Action<object> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Volatile.Write(ref _afterDeserialize, callback);
    }

    internal static void RegisterRuntimeTypes(Func<Type, bool> isEngineObject,
        Func<string, Type, object> deserializeScalar)
    {
        ArgumentNullException.ThrowIfNull(isEngineObject);
        ArgumentNullException.ThrowIfNull(deserializeScalar);
        Volatile.Write(ref _isEngineObject, isEngineObject);
        Volatile.Write(ref _deserializeScalar, deserializeScalar);
    }

    internal static bool IsEngineObject(Type type) =>
        Volatile.Read(ref _isEngineObject)?.Invoke(type) ?? false;

    internal static object DeserializeScalar(string text, Type type) =>
        (Volatile.Read(ref _deserializeScalar) ??
         throw new InvalidOperationException("The engine scalar codec has not been registered."))(text, type);

    internal static void AfterDeserialize(object value)
    {
        var callback = Volatile.Read(ref _afterDeserialize) ??
            throw new InvalidOperationException("The engine serialization callbacks have not been registered.");
        callback(value);
    }
}
