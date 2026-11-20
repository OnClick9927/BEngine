namespace BEngine.Serialization;

internal static class SerializationModelDefaults
{
    private static Func<string> _transformTypeName = static () => "BEngine.Transform";

    internal static string TransformTypeName() => _transformTypeName();

    internal static void Register(Func<string> transformTypeName)
    {
        _transformTypeName = transformTypeName ?? throw new ArgumentNullException(nameof(transformTypeName));
    }
}
