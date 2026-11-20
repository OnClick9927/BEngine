using System.Collections;

namespace BEngine;

public sealed class Coroutine : BObject
{
    internal MonoBehaviour Owner { get; }
    internal IEnumerator Routine { get; }
    internal string? MethodName { get; }

    internal Coroutine(MonoBehaviour owner, IEnumerator routine, string? methodName = null)
    {
        Owner = owner;
        Routine = routine;
        MethodName = methodName;
        name = methodName ?? routine.GetType().Name;
    }
}
