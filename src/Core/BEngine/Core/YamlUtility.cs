using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BEngine;

public interface ISerializationCallbackReceiver
{
    void OnBeforeSerialize();
    void OnAfterDeserialize();
}
internal static class SerializationCallbackUtility
{
    private static int _beforeSerializeSuppression;
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

public static class YamlUtility
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithTypeConverter(new TextureAtlasYamlConverter())
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .WithTypeConverter(new TextureAtlasYamlConverter())
        .Build();

    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BEngine.SerializationCallbackUtility.BeforeSerialize(value);
        return Serializer.Serialize(value);
    }

    public static T Deserialize<T>(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        var value = Deserializer.Deserialize<T>(yaml) ??
                    throw new InvalidDataException($"The YAML document for {typeof(T).Name} is empty.");
        BEngine.SerializationCallbackUtility.AfterDeserialize(value);
        return value;
    }

    public static object Deserialize(string yaml, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        ArgumentNullException.ThrowIfNull(type);
        var value = Deserializer.Deserialize(new StringReader(yaml), type) ??
                    throw new InvalidDataException($"The YAML document for {type.Name} is empty.");
        BEngine.SerializationCallbackUtility.AfterDeserialize(value);
        return value;
    }

    public static T Load<T>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize<T>(File.ReadAllText(Path.GetFullPath(path)));
    }

    public static object Load(string path, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllText(Path.GetFullPath(path)), type);
    }

    public static void Save(object value, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (value is BObject { IsRuntimeOnly: true })
            throw new InvalidOperationException("Runtime objects are transient and cannot be saved.");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, Serialize(value));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
