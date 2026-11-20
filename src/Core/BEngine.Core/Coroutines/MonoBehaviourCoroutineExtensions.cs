using System.Collections;

namespace BEngine;

/// <summary>Optional coroutine and delayed invocation APIs for any MonoBehaviour.</summary>
public static class MonoBehaviourCoroutineExtensions
{
    public static Coroutine StartCoroutine(this MonoBehaviour owner, IEnumerator routine) => CoroutineScheduler.Start(owner, routine);
    public static Coroutine? StartCoroutine(this MonoBehaviour owner, string methodName) => CoroutineScheduler.Start(owner, methodName);
    public static void StopCoroutine(this MonoBehaviour owner, Coroutine routine) => CoroutineScheduler.Stop(owner, routine);
    public static void StopCoroutine(this MonoBehaviour owner, IEnumerator routine) => CoroutineScheduler.Stop(owner, routine);
    public static void StopCoroutine(this MonoBehaviour owner, string methodName) => CoroutineScheduler.Stop(owner, methodName);
    public static void StopAllCoroutines(this MonoBehaviour owner) => CoroutineScheduler.StopAll(owner);
    public static void Invoke(this MonoBehaviour owner, string methodName, Fix64 time) => CoroutineScheduler.Invoke(owner, methodName, time, null);
    public static void InvokeRepeating(this MonoBehaviour owner, string methodName, Fix64 time, Fix64 repeatRate) => CoroutineScheduler.Invoke(owner, methodName, time, repeatRate);
    public static void CancelInvoke(this MonoBehaviour owner) => CoroutineScheduler.CancelInvokes(owner);
    public static void CancelInvoke(this MonoBehaviour owner, string methodName) => CoroutineScheduler.CancelInvokes(owner, methodName);
    public static bool IsInvoking(this MonoBehaviour owner) => CoroutineScheduler.IsInvoking(owner);
    public static bool IsInvoking(this MonoBehaviour owner, string methodName) => CoroutineScheduler.IsInvoking(owner, methodName);
}
