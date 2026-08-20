using System.Collections;

namespace BEngine;

public abstract class MonoBehaviour : Behaviour
{
    internal SceneRuntimeTransferState? SceneTransferState { get; set; }
    private bool _runInEditMode;

    public bool runInEditMode
    {
        get { MainThreadGuard.Ensure(); return _runInEditMode; }
        set { MainThreadGuard.Ensure(); _runInEditMode = value; }
    }

    public Coroutine StartCoroutine(IEnumerator routine)
    {
        MainThreadGuard.Ensure();
        return CoroutineScheduler.Start(this, routine);
    }
    public Coroutine? StartCoroutine(string methodName)
    {
        MainThreadGuard.Ensure();
        return CoroutineScheduler.Start(this, methodName);
    }
    public void StopCoroutine(Coroutine routine)
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.Stop(this, routine);
    }
    public void StopCoroutine(IEnumerator routine)
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.Stop(this, routine);
    }
    public void StopCoroutine(string methodName)
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.Stop(this, methodName);
    }
    public void StopAllCoroutines()
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.StopAll(this);
    }
    public void Invoke(string methodName, Fix64 time)
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.Invoke(this, methodName, time, null);
    }
    public void InvokeRepeating(string methodName, Fix64 time, Fix64 repeatRate)
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.Invoke(this, methodName, time, repeatRate);
    }
    public void CancelInvoke()
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.CancelInvokes(this);
    }
    public void CancelInvoke(string methodName)
    {
        MainThreadGuard.Ensure();
        CoroutineScheduler.CancelInvokes(this, methodName);
    }
    public bool IsInvoking()
    {
        MainThreadGuard.Ensure();
        return CoroutineScheduler.IsInvoking(this);
    }
    public bool IsInvoking(string methodName)
    {
        MainThreadGuard.Ensure();
        return CoroutineScheduler.IsInvoking(this, methodName);
    }
    public static void print(object? message) => Debug.Log(message?.ToString() ?? "null");

    public virtual void Awake() { }
    public virtual void OnEnable() { }
    public virtual void Start() { }
    public virtual void FixedUpdate() { }
    public virtual void Update() { }
    public virtual void LateUpdate() { }
    public virtual void OnDisable() { }
    public virtual void OnApplicationFocus(bool hasFocus) { }
    public virtual void OnApplicationPause(bool pauseStatus) { }
    public virtual void OnApplicationQuit() { }
    public virtual void OnTransformParentChanged() { }
    public virtual void OnTransformChildrenChanged() { }
    public virtual void OnValidate() { }
    public virtual void Reset() { }
    public virtual void OnDestroy() { }
}
