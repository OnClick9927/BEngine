using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

// Estimates retained managed payloads, rather than claiming exact GC heap sizes.
// Walk fields only: measuring a snapshot must never invoke user property getters.
internal static class UndoMemoryEstimate
{
    private static readonly Dictionary<Type, (int Size, bool References)> Layouts = [];
    private static readonly MethodInfo LayoutMethod = typeof(UndoMemoryEstimate).GetMethod(nameof(Layout), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static long Measure(params object?[] roots)
    {
        var pending = new Stack<object?>(roots);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        long bytes = 0;
        while (pending.TryPop(out var value))
        {
            if (value is null) continue;
            if (value is BObject or Type or MemberInfo) { bytes += IntPtr.Size; continue; }
            if (!visited.Add(value)) continue;
            if (value is string text) { bytes += 24L + text.Length * 2L; continue; }
            if (value is ObjectState state) { bytes += state.EstimatedBytes; continue; }
            if (value is Delegate callback)
            {
                bytes += 64; pending.Push(callback.Target); continue;
            }
            var type = value.GetType();
            var layout = GetLayout(type);
            if (value is Array array)
            {
                var element = GetLayout(type.GetElementType()!);
                bytes += 24L + array.Rank * 8L + array.LongLength * element.Size;
                if (element.References) foreach (var item in array) pending.Push(item);
                continue;
            }
            if (value is IDictionary dictionary)
            {
                bytes += 80L + dictionary.Count * 48L;
                foreach (DictionaryEntry item in dictionary) { pending.Push(item.Key); pending.Push(item.Value); }
                continue;
            }
            if (value is IList list)
            {
                bytes += 48L + list.Count * (long)IntPtr.Size;
                foreach (var item in list) pending.Push(item);
                continue;
            }
            bytes += 24 + layout.Size;
            if (!layout.References || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) continue;
            for (var declaring = type; declaring is not null && declaring != typeof(object); declaring = declaring.BaseType)
                foreach (var field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    pending.Push(field.GetValue(value));
        }
        return bytes;
    }
    private static (int Size, bool References) GetLayout(Type type)
    {
        if (!type.IsValueType) return (IntPtr.Size, true);
        if (!Layouts.TryGetValue(type, out var layout))
            Layouts.Add(type, layout = ((int, bool))LayoutMethod.MakeGenericMethod(type).Invoke(null, null)!);
        return layout;
    }
    private static (int, bool) Layout<T>() => (Unsafe.SizeOf<T>(), RuntimeHelpers.IsReferenceOrContainsReferences<T>());
}
