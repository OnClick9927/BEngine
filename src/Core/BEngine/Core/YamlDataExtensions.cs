namespace BEngine;

/// <summary>Persistence helpers for non-asset editor and project data.</summary>
public static class YamlDataExtensions
{
    public static string ToYaml<TData>(this TData value) where TData : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return YamlUtility.Serialize(value);
    }

    public static void Save<TData>(this TData value, string path) where TData : class
    {
        ArgumentNullException.ThrowIfNull(value);
        YamlUtility.Save(value, path);
    }
}
