namespace BEngine;

public abstract class MonoBehaviour : Behaviour
{
    internal SceneRuntimeTransferState? SceneTransferState { get; set; }
    private bool _runInEditMode;

    public bool runInEditMode
    {
        get { return _runInEditMode; }
        set { _runInEditMode = value; }
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
    public override void OnReset() => Reset();
    public virtual void Reset() { }
    public virtual void OnDestroy() { }
}
