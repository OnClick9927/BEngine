using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class SessionState
{
    private static readonly Dictionary<string, object> Values = new(StringComparer.Ordinal);

    public static void EraseString(string key) => Values.Remove(key);
    public static string GetString(string key, string defaultValue) => Values.TryGetValue(key, out var value)
        ? value?.ToString() ?? defaultValue : defaultValue;
    public static void SetString(string key, string value) => Values[key] = value;
    public static int GetInt(string key, int defaultValue) => Values.TryGetValue(key, out var value) && value is int typed
        ? typed : defaultValue;
    public static void SetInt(string key, int value) => Values[key] = value;
    public static float GetFloat(string key, float defaultValue) =>
        Values.TryGetValue(key, out var value) && value is float typed ? typed : defaultValue;
    public static void SetFloat(string key, float value) => Values[key] = value;
    public static bool GetBool(string key, bool defaultValue) =>
        Values.TryGetValue(key, out var value) && value is bool typed ? typed : defaultValue;
    public static void SetBool(string key, bool value) => Values[key] = value;
}
