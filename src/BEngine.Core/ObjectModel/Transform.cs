namespace BEngine;

public class Transform : Component
{
    private readonly List<Transform> _children = [];
    private Vector3 _localPosition = Vector3.zero;
    private Quaternion _localRotation = Quaternion.identity;
    private Vector3 _localEulerAngles = Vector3.zero;
    private Vector3 _localScale = Vector3.one;

    public Transform? parent { get; private set; }
    public IReadOnlyList<Transform> children => _children;
    public int childCount => _children.Count;
    public Transform root => parent?.root ?? this;

    public Vector3 localPosition
    {
        get => _localPosition;
        set => _localPosition = value;
    }

    public Quaternion localRotation
    {
        get => _localRotation;
        set => _localRotation = value.normalized;
    }

    public Vector3 localEulerAngles
    {
        get => _localEulerAngles;
        set
        {
            _localEulerAngles = value;
            _localRotation = Quaternion.Euler(value);
        }
    }

    public Vector3 localScale
    {
        get => _localScale;
        set => _localScale = value;
    }

    public Vector3 position
    {
        get => parent is null
            ? localPosition
            : parent.position + (parent.rotation * Vector3.Scale(parent.lossyScale, localPosition));
        set
        {
            if (parent is null)
            {
                localPosition = value;
                return;
            }

            var relative = Quaternion.Inverse(parent.rotation) * (value - parent.position);
            var scale = parent.lossyScale;
            localPosition = new Vector3(
                scale.x == 0 ? 0 : relative.x / scale.x,
                scale.y == 0 ? 0 : relative.y / scale.y,
                scale.z == 0 ? 0 : relative.z / scale.z);
        }
    }

    public Quaternion rotation
    {
        get => parent is null ? localRotation : parent.rotation * localRotation;
        set => localRotation = parent is null ? value : Quaternion.Inverse(parent.rotation) * value;
    }

    public Vector3 lossyScale => parent is null ? localScale : Vector3.Scale(parent.lossyScale, localScale);
    public Vector3 forward => rotation * Vector3.forward;
    public Vector3 up => rotation * Vector3.up;
    public Vector3 right => rotation * Vector3.right;

    public Transform GetChild(int index) => _children[index];

    public void SetParent(Transform? newParent, bool worldPositionStays = true)
    {
        if (ReferenceEquals(newParent, this) || IsDescendantOf(newParent))
        {
            throw new InvalidOperationException("A Transform cannot be parented to itself or one of its descendants.");
        }

        var worldPosition = position;
        var worldRotation = rotation;
        parent?._children.Remove(this);
        parent = newParent;
        parent?._children.Add(this);

        if (worldPositionStays)
        {
            position = worldPosition;
            rotation = worldRotation;
        }
    }

    public void Translate(Vector3 translation) => position += translation;

    public void Translate(Vector3 translation, Space relativeTo)
    {
        position += relativeTo == Space.Self ? rotation * translation : translation;
    }

    public void Rotate(Vector3 eulerAngles, Space relativeTo = Space.Self)
    {
        var delta = Quaternion.Euler(eulerAngles);
        rotation = relativeTo == Space.Self ? rotation * delta : delta * rotation;
        _localEulerAngles += eulerAngles;
    }

    public void SetPositionAndRotation(Vector3 worldPosition, Quaternion worldRotation)
    {
        position = worldPosition;
        rotation = worldRotation;
    }

    public Vector3 TransformPoint(Vector3 point) =>
        position + (rotation * Vector3.Scale(lossyScale, point));

    public Vector3 InverseTransformPoint(Vector3 point)
    {
        var relative = Quaternion.Inverse(rotation) * (point - position);
        var scale = lossyScale;
        return new Vector3(
            scale.x == 0 ? 0 : relative.x / scale.x,
            scale.y == 0 ? 0 : relative.y / scale.y,
            scale.z == 0 ? 0 : relative.z / scale.z);
    }

    public bool IsChildOf(Transform potentialParent)
    {
        ArgumentNullException.ThrowIfNull(potentialParent);
        for (var current = parent; current is not null; current = current.parent)
        {
            if (ReferenceEquals(current, potentialParent)) return true;
        }
        return false;
    }

    public int GetSiblingIndex() => parent?._children.IndexOf(this) ??
        gameObject.scene?.rootGameObjects.ToList().IndexOf(gameObject) ?? 0;

    public void SetSiblingIndex(int index)
    {
        if (parent is null) return;
        parent._children.Remove(this);
        parent._children.Insert(Math.Clamp(index, 0, parent._children.Count), this);
    }

    internal void TransferTo(Transform replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        replacement._localPosition = _localPosition;
        replacement._localRotation = _localRotation;
        replacement._localEulerAngles = _localEulerAngles;
        replacement._localScale = _localScale;

        if (parent is not null)
        {
            var index = parent._children.IndexOf(this);
            if (index >= 0) parent._children[index] = replacement;
            replacement.parent = parent;
        }

        foreach (var child in _children)
        {
            child.parent = replacement;
            replacement._children.Add(child);
        }

        parent = null;
        _children.Clear();
    }

    private bool IsDescendantOf(Transform? candidate)
    {
        for (var current = candidate; current is not null; current = current.parent)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }

        return false;
    }
}

public enum Space
{
    World,
    Self
}
