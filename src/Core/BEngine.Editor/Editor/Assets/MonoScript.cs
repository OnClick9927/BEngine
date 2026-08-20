using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

public sealed class MonoScript : TextAsset
{
    internal Type? scriptClass { get; set; }
    public Type? GetClass() => scriptClass;

    public static MonoScript? FromMonoBehaviour(MonoBehaviour behaviour) => FromType(behaviour?.GetType());
    public static MonoScript? FromScriptableObject(ScriptableObject scriptableObject) =>
        FromType(scriptableObject?.GetType());

    private static MonoScript? FromType(Type? type)
    {
        if (type is null) return null;
        return AssetDatabase.FindAssets($"{type.Name} t:Script")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<MonoScript>)
            .FirstOrDefault(script => script?.GetClass() == type);
    }
}
