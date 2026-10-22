using System.Collections;

namespace BEngine;

public abstract class CustomYieldInstruction : IEnumerator
{
    public abstract bool keepWaiting { get; }
    public object? Current => null;
    public bool MoveNext() => keepWaiting;
    public void Reset() { }
}
