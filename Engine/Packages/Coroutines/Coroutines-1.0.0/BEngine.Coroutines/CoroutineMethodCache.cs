using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace BEngine;

internal static class CoroutineMethodCache
{
    // Weak type keys allow gameplay assemblies to unload after an editor reload.
    private static readonly ConditionalWeakTable<Type, Dictionary<string, Func<object, IEnumerator>>> Factories = new();

    internal static bool TryInvoke(MonoBehaviour target, string methodName, out IEnumerator routine)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var factories = Factories.GetValue(target.GetType(), Build);
        if (!factories.TryGetValue(methodName, out var factory)) { routine = null!; return false; }
        routine = factory(target);
        return routine is not null;
    }

    private static Dictionary<string, Func<object, IEnumerator>> Build(Type type)
    {
        var factories = new Dictionary<string, Func<object, IEnumerator>>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType)
        foreach (var method in current.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.ContainsGenericParameters || !typeof(IEnumerator).IsAssignableFrom(method.ReturnType) ||
                method.GetParameters().Length != 0 || factories.ContainsKey(method.Name)) continue;
            var dynamicMethod = new DynamicMethod($"Coroutine_{method.Name}_{method.MetadataToken}",
                typeof(IEnumerator), [typeof(object)], typeof(CoroutineMethodCache).Module, skipVisibility: true);
            var il = dynamicMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, method.DeclaringType!);
            il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
            if (method.ReturnType != typeof(IEnumerator)) il.Emit(OpCodes.Castclass, typeof(IEnumerator));
            il.Emit(OpCodes.Ret);
            factories.Add(method.Name, (Func<object, IEnumerator>)dynamicMethod.CreateDelegate(typeof(Func<object, IEnumerator>)));
        }
        return factories;
    }
}
