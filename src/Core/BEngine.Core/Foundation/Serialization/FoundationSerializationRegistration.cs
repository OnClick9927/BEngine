using System.Runtime.CompilerServices;

namespace BEngine;

internal static class FoundationSerializationRegistration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Serialization.SerializationModelDefaults.Register(static () => typeof(Transform).FullName!);
        SerializationMetadata.RegisterObjectMembers(static type =>
            RuntimeTypeCache.GetInstanceMembers(type).Where(RuntimeTypeCache.IsSerializableMember));
        SerializationCallbackBridge.RegisterAfterDeserialize(static value =>
            SerializationCallbackUtility.AfterDeserialize(value));
        SerializationCallbackBridge.RegisterRuntimeTypes(
            static type => typeof(BObject).IsAssignableFrom(type),
            static (text, type) => YamlUtility.Deserialize(text, type));
    }
}
