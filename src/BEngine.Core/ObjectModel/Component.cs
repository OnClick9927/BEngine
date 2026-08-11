namespace BEngine;

public abstract class Component : BObject
{
    private GameObject? _gameObject;

    public GameObject gameObject => _gameObject ??
        throw new InvalidOperationException("Component is not attached to a GameObject.");
    public Transform transform => gameObject.transform;
    public bool enabled { get; set; } = true;

    public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    public Component? GetComponent(Type type) => gameObject.GetComponent(type);
    public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>().ToArray();
    public T? GetComponentInParent<T>() where T : Component => gameObject.GetComponentInParent<T>();
    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component =>
        gameObject.GetComponentInChildren<T>(includeInactive);
    public bool TryGetComponent<T>(out T? component) where T : Component =>
        gameObject.TryGetComponent(out component);
    public bool CompareTag(string value) => gameObject.CompareTag(value);

    internal void Attach(GameObject owner)
    {
        if (_gameObject is not null)
        {
            throw new InvalidOperationException("A Component can only be attached once.");
        }

        _gameObject = owner;
        name = GetType().Name;
    }
}

public abstract class Behaviour : Component;

public abstract class MonoBehaviour : Behaviour
{
    public virtual void Awake() { }
    public virtual void OnEnable() { }
    public virtual void Start() { }
    public virtual void FixedUpdate() { }
    public virtual void Update() { }
    public virtual void LateUpdate() { }
    public virtual void OnDisable() { }
    public virtual void OnApplicationQuit() { }
    public virtual void OnDestroy() { }
}
