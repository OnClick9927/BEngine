using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

public sealed class MonoScript : Script
{
    public static MonoScript? FromMonoBehaviour(MonoBehaviour behaviour) => FromType(behaviour?.GetType());
    public static MonoScript? FromScriptableObject(ScriptableObject scriptableObject) =>
        FromType(scriptableObject?.GetType());

    private static MonoScript? FromType(Type? type)
    {
        if (type is null) return null;
        return Matches482();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Editor.MonoScript? Matches482()
        {
            var matchSource482 = AssetDatabase.FindAssets($"{type.Name} t:Script").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<MonoScript>);
            ArgumentNullException.ThrowIfNull(matchSource482, "source");
            global::System.ReadOnlySpan<global::BEngine.Editor.MonoScript> matchValues482;
            if (matchSource482.GetType() == typeof(global::BEngine.Editor.MonoScript[]))
            {
                matchValues482 = (global::BEngine.Editor.MonoScript[])(object)matchSource482;
            }
            else if (matchSource482.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.MonoScript>))
            {
                matchValues482 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.MonoScript>)(object)matchSource482);
            }
            else
            {
                foreach (var script in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.MonoScript>)matchSource482)
                {
                    if (script?.GetClass() == type)
                        return script;
                }
        
                return default!;
            }
        
            foreach (var script in matchValues482)
            {
                if (script?.GetClass() == type)
                    return script;
            }
        
            return default!;
        }
    }
}
