using System.Linq.Expressions;
using System.Reflection;

namespace BEngine.Editor;

internal static class ReflectionCacheDictionaryExtensions
{
    internal static List<TValue> GetOrAdd<TKey, TValue>(
        this Dictionary<TKey, List<TValue>> dictionary,
        TKey key) where TKey : notnull
    {
        if (dictionary.TryGetValue(key, out var values)) return values;
        values = [];
        dictionary.Add(key, values);
        return values;
    }
}
