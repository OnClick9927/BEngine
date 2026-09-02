using System.Collections;

namespace BEngine;

internal static class CoroutineScheduler
{
    private static readonly List<Entry> Entries = [];
    private static readonly List<Invocation> Invocations = [];
    private static readonly List<Entry> TickEntries = [];
    private static readonly List<Invocation> TickInvocations = [];

    internal static Coroutine Start(MonoBehaviour owner, IEnumerator routine, string? methodName = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(routine);
        var coroutine = new Coroutine(owner, routine, methodName);
        Entries.Add(new Entry(coroutine));
        return coroutine;
    }

    internal static Coroutine? Start(MonoBehaviour owner, string methodName)
    {
        if (!RuntimeTypeCache.TryInvokeCoroutine(owner, methodName, out var routine))
            throw new InvalidOperationException($"{owner.GetType().Name}.{methodName} must return IEnumerator.");
        return Start(owner, routine, methodName);
    }

    internal static void Stop(MonoBehaviour owner, Coroutine coroutine)
    {
        if (coroutine is null) return;
        RemoveEntries(entry => ReferenceEquals(entry.Coroutine, coroutine) &&
                               ReferenceEquals(entry.Coroutine.Owner, owner));
    }

    internal static void Stop(MonoBehaviour owner, IEnumerator routine)
    {
        if (routine is null) return;
        RemoveEntries(entry => ReferenceEquals(entry.Coroutine.Owner, owner) &&
                               ReferenceEquals(entry.Coroutine.Routine, routine));
    }

    internal static void Stop(MonoBehaviour owner, string methodName)
    {
        RemoveEntries(entry => ReferenceEquals(entry.Coroutine.Owner, owner) &&
                               entry.Coroutine.MethodName == methodName);
    }

    internal static void StopAll(MonoBehaviour owner)
    {
        RemoveEntries(entry => ReferenceEquals(entry.Coroutine.Owner, owner));
        RemoveInvocations(entry => ReferenceEquals(entry.Owner, owner));
    }

    internal static void Invoke(MonoBehaviour owner, string methodName, Fix64 delay, Fix64? repeatRate)
    {
        if (!RuntimeTypeCache.HasMessage(owner.GetType(), methodName, hasArgument: false))
            throw new MissingMethodException(owner.GetType().FullName, methodName);
        if (repeatRate is { } rate && rate <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(repeatRate));
        Invocations.Add(new Invocation(owner, methodName,
            Time.time + Fix64.Max(Fix64.Zero, delay), repeatRate));
    }

    internal static void CancelInvokes(MonoBehaviour owner, string? methodName = null)
    {
        RemoveInvocations(entry => ReferenceEquals(entry.Owner, owner) &&
                                   (methodName is null || entry.MethodName == methodName));
    }

    internal static bool IsInvoking(MonoBehaviour owner, string? methodName = null)
    {
        return Invocations.Any(entry => ReferenceEquals(entry.Owner, owner) &&
                                        (methodName is null || entry.MethodName == methodName));
    }

    internal static void Tick(Scene scene)
    {
        if (Entries.Count == 0 && Invocations.Count == 0) return;
        TickEntries.Clear();
        TickInvocations.Clear();
        foreach (var entry in Entries)
            if (ReferenceEquals(entry.Coroutine.Owner.gameObject.scene, scene)) TickEntries.Add(entry);
        foreach (var invocation in Invocations)
            if (ReferenceEquals(invocation.Owner.gameObject.scene, scene) && invocation.NextTime <= Time.time)
                TickInvocations.Add(invocation);

        foreach (var invocation in TickInvocations)
            if (invocation.IsScheduled) RunInvocation(invocation);
        foreach (var entry in TickEntries)
        {
            if (!entry.IsScheduled) continue;
            if (!entry.Coroutine.Owner.enabled || !entry.Coroutine.Owner.gameObject.activeInHierarchy) continue;
            try
            {
                if (!entry.Step()) Remove(entry);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Coroutine {entry.Coroutine.name} failed: {exception.Message}");
                Remove(entry);
            }
        }
        TickEntries.Clear();
        TickInvocations.Clear();
    }

    internal static void StopScene(Scene scene)
    {
        RemoveEntries(entry => ReferenceEquals(entry.Coroutine.Owner.gameObject.scene, scene));
        RemoveInvocations(entry => ReferenceEquals(entry.Owner.gameObject.scene, scene));
    }

    private static void RunInvocation(Invocation invocation)
    {
        try { RuntimeTypeCache.TryInvokeMessage(invocation.Owner, invocation.MethodName); }
        catch (Exception exception)
        {
            Debug.LogError($"Invoke {invocation.Owner.GetType().Name}.{invocation.MethodName} failed: " +
                           exception.Message);
        }
        if (!invocation.IsScheduled) return;
        RemoveInvocation(invocation);
        if (invocation.RepeatRate is not { } repeatRate) return;
        invocation.NextTime = Time.time + repeatRate;
        invocation.IsScheduled = true;
        Invocations.Add(invocation);
    }

    private static void Remove(Entry entry)
    {
        entry.IsScheduled = false;
        Entries.Remove(entry);
    }

    private static void RemoveEntries(Predicate<Entry> predicate)
    {
        for (var index = Entries.Count - 1; index >= 0; index--)
        {
            var entry = Entries[index];
            if (!predicate(entry)) continue;
            entry.IsScheduled = false;
            Entries.RemoveAt(index);
        }
    }

    private static void RemoveInvocation(Invocation invocation)
    {
        invocation.IsScheduled = false;
        Invocations.Remove(invocation);
    }

    private static void RemoveInvocations(Predicate<Invocation> predicate)
    {
        for (var index = Invocations.Count - 1; index >= 0; index--)
        {
            var invocation = Invocations[index];
            if (!predicate(invocation)) continue;
            invocation.IsScheduled = false;
            Invocations.RemoveAt(index);
        }
    }

    private sealed class Entry
    {
        private readonly Stack<IEnumerator> _stack = [];
        private Fix64 _waitTime;
        private CustomYieldInstruction? _customYield;
        private bool _waitOneFrame;

        internal Coroutine Coroutine { get; }
        internal bool IsScheduled { get; set; } = true;

        internal Entry(Coroutine coroutine)
        {
            Coroutine = coroutine;
            _stack.Push(coroutine.Routine);
        }

        internal bool Step()
        {
            if (_waitOneFrame) _waitOneFrame = false;
            else if (_waitTime > Fix64.Zero)
            {
                _waitTime -= Time.deltaTime;
                if (_waitTime > Fix64.Zero) return true;
            }
            else if (_customYield is not null)
            {
                if (_customYield.keepWaiting) return true;
                _customYield = null;
            }

            while (_stack.Count > 0)
            {
                var iterator = _stack.Peek();
                if (!iterator.MoveNext())
                {
                    _stack.Pop();
                    continue;
                }
                switch (iterator.Current)
                {
                    case CustomYieldInstruction custom:
                        _customYield = custom;
                        return true;
                    case IEnumerator nested:
                        _stack.Push(nested);
                        continue;
                    case WaitForSeconds wait:
                        _waitTime = wait.seconds;
                        return true;
                    default:
                        _waitOneFrame = true;
                        return true;
                }
            }
            return false;
        }
    }

    private sealed class Invocation(
        MonoBehaviour owner,
        string methodName,
        Fix64 nextTime,
        Fix64? repeatRate)
    {
        internal MonoBehaviour Owner { get; } = owner;
        internal string MethodName { get; } = methodName;
        internal Fix64 NextTime { get; set; } = nextTime;
        internal Fix64? RepeatRate { get; } = repeatRate;
        internal bool IsScheduled { get; set; } = true;
    }
}
