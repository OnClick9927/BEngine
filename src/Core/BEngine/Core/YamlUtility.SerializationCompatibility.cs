namespace BEngine.Serialization;

/// <summary>
/// Preserves the public serialization namespace after YamlUtility moved into the BEngine root.
/// </summary>
public static class YamlUtility
{
    public static string Serialize(object value) => BEngine.YamlUtility.Serialize(value);

    public static T Deserialize<T>(string yaml) => BEngine.YamlUtility.Deserialize<T>(yaml);

    public static object Deserialize(string yaml, Type type) =>
        BEngine.YamlUtility.Deserialize(yaml, type);

    public static T Load<T>(string path) => BEngine.YamlUtility.Load<T>(path);

    public static object Load(string path, Type type) => BEngine.YamlUtility.Load(path, type);

    public static void Save(object value, string path) => BEngine.YamlUtility.Save(value, path);
}
