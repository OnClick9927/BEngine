namespace BEngine;

public class Transform : Component
{
    private readonly List<Transform> _children = [];
    private readonly IReadOnlyList<Transform> _childrenView;
    private Transform? _parent;
    private Vector2 _localPosition = Vector2.zero;
    private Fix64 _localRotation;
    private Vector2 _localScale = Vector2.one;

    public Transform() => _childrenView = _children.AsReadOnly();

    public Transform? parent
    {
        get { return _parent; }
        private set => _parent = value;
    }
    public IReadOnlyList<Transform> children
    {
        get { return _childrenView; }
    }
    public int childCount
    {
        get { return _children.Count; }
    }
    public Transform root
    {
        get { return RootUnchecked; }
    }

    internal Transform? ParentUnchecked => _parent;
    internal IReadOnlyList<Transform> ChildrenUnchecked => _children;
    internal Transform RootUnchecked => _parent?.RootUnchecked ?? this;
    internal Vector2 LocalPositionUnchecked => _localPosition;
    internal Fix64 LocalRotationUnchecked => _localRotation;
    internal Vector2 LocalScaleUnchecked => _localScale;
    internal void GetWorldPoseUnchecked(out Vector2 worldPosition, out Fix64 worldRotation,
        out Vector2 worldScale) => GetWorldPoseCore(out worldPosition, out worldRotation, out worldScale);

    public Vector2 localPosition
    {
        get { return GetLocalPositionCore(); }
        set { SetLocalPositionCore(value); }
    }
    public Fix64 localRotation
    {
        get { return GetLocalRotationCore(); }
        set { SetLocalRotationCore(value); }
    }
    public Vector2 localScale
    {
        get { return GetLocalScaleCore(); }
        set { SetLocalScaleCore(value); }
    }
    public Vector2 position
    {
        get { return GetPositionCore(); }
        set { SetPositionCore(value); }
    }
    public Fix64 rotation
    {
        get { return GetRotationCore(); }
        set { SetRotationCore(value); }
    }
    public Vector2 lossyScale
    {
        get { return GetLossyScaleCore(); }
    }
    public Vector2 right
    {
        get { return RotateVector(Vector2.right, GetRotationCore()); }
    }
    public Vector2 up
    {
        get { return RotateVector(Vector2.up, GetRotationCore()); }
    }

    public Transform GetChild(int index)
    {
        return _children[index];
    }
    public void SetParent(Transform? newParent, bool worldPositionStays = true)
    {
        SetParentCore(newParent, worldPositionStays);
    }
    public void Translate(Vector2 translation, Space relativeTo = Space.Self)
    {
        var delta = relativeTo == Space.Self ? RotateVector(translation, GetRotationCore()) : translation;
        SetPositionCore(GetPositionCore() + delta);
    }
    public void Translate(Fix64 x, Fix64 y, Space relativeTo = Space.Self) =>
        Translate(new Vector2(x, y), relativeTo);
    public void Rotate(Fix64 degrees, Space relativeTo = Space.Self)
    {
        if (relativeTo == Space.World && _parent is not null) SetRotationCore(GetRotationCore() + degrees);
        else SetLocalRotationCore(GetLocalRotationCore() + degrees);
    }
    public void SetPositionAndRotation(Vector2 worldPosition, Fix64 worldRotation)
    {
        SetPositionCore(worldPosition);
        SetRotationCore(worldRotation);
    }
    public Vector2 TransformPoint(Vector2 point)
    {
        return GetPositionCore() + RotateVector(Vector2.Scale(GetLossyScaleCore(), point), GetRotationCore());
    }
    public Vector2 InverseTransformPoint(Vector2 point)
    {
        return DivideByScale(RotateVector(point - GetPositionCore(), -GetRotationCore()), GetLossyScaleCore());
    }
    public Vector2 TransformDirection(Vector2 direction)
    {
        return RotateVector(direction, GetRotationCore());
    }
    public Vector2 InverseTransformDirection(Vector2 direction)
    {
        return RotateVector(direction, -GetRotationCore());
    }
    public Vector2 TransformVector(Vector2 vector)
    {
        return RotateVector(Vector2.Scale(GetLossyScaleCore(), vector), GetRotationCore());
    }
    public Vector2 InverseTransformVector(Vector2 vector)
    {
        return DivideByScale(RotateVector(vector, -GetRotationCore()), GetLossyScaleCore());
    }
    public void DetachChildren()
    {
        foreach (var child in _children.ToArray()) child.SetParentCore(null, true);
    }
    public bool IsChildOf(Transform potentialParent)
    {
        ArgumentNullException.ThrowIfNull(potentialParent);
        for (var current = _parent; current is not null; current = current._parent)
            if (ReferenceEquals(current, potentialParent)) return true;
        return false;
    }
    public int GetSiblingIndex()
    {
        return _parent?._children.IndexOf(this) ??
            GameObjectUnchecked.SceneUnchecked?.RootIndexOfUnchecked(GameObjectUnchecked) ?? 0;
    }
    public void SetSiblingIndex(int index)
    {
        SetSiblingIndexCore(index);
    }
    public void SetAsFirstSibling() => SetSiblingIndex(0);
    public void SetAsLastSibling() => SetSiblingIndex(_parent?._children.Count - 1 ?? 0);

    internal void TransferTo(Transform replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        replacement._localPosition = _localPosition;
        replacement._localRotation = _localRotation;
        replacement._localScale = _localScale;
        if (_parent is not null)
        {
            var index = _parent._children.IndexOf(this);
            if (index >= 0) _parent._children[index] = replacement;
            replacement._parent = _parent;
        }
        foreach (var child in _children)
        {
            child._parent = replacement;
            replacement._children.Add(child);
        }
        _parent = null;
        _children.Clear();
    }
    private Vector2 GetLocalPositionCore() => _localPosition;
    private Fix64 GetLocalRotationCore() => _localRotation;
    private Vector2 GetLocalScaleCore() => _localScale;
    private void SetLocalPositionCore(Vector2 value) => _localPosition = value;
    private void SetLocalRotationCore(Fix64 value)
    {
        _localRotation = NormalizeDegrees(value);
    }
    private void SetLocalScaleCore(Vector2 value) => _localScale = value;
    private Vector2 GetPositionCore() => _parent is null
        ? GetLocalPositionCore()
        : _parent.GetPositionCore() + RotateVector(
            Vector2.Scale(_parent.GetLossyScaleCore(), GetLocalPositionCore()), _parent.GetRotationCore());
    private void SetPositionCore(Vector2 value)
    {
        if (_parent is null) SetLocalPositionCore(value);
        else SetLocalPositionCore(DivideByScale(
            RotateVector(value - _parent.GetPositionCore(), -_parent.GetRotationCore()),
            _parent.GetLossyScaleCore()));
    }
    private Fix64 GetRotationCore() => _parent is null
        ? GetLocalRotationCore()
        : NormalizeDegrees(_parent.GetRotationCore() + GetLocalRotationCore());
    private void SetRotationCore(Fix64 value) => SetLocalRotationCore(_parent is null
        ? value
        : value - _parent.GetRotationCore());
    private Vector2 GetLossyScaleCore() => _parent is null
        ? GetLocalScaleCore()
        : Vector2.Scale(_parent.GetLossyScaleCore(), GetLocalScaleCore());
    private void GetWorldPoseCore(out Vector2 worldPosition, out Fix64 worldRotation, out Vector2 worldScale)
    {
        if (_parent is null)
        {
            worldPosition = _localPosition;
            worldRotation = _localRotation;
            worldScale = _localScale;
            return;
        }

        _parent.GetWorldPoseCore(out var parentPosition, out var parentRotation, out var parentScale);
        worldPosition = parentPosition + RotateVector(
            Vector2.Scale(parentScale, _localPosition), parentRotation);
        worldRotation = NormalizeDegrees(parentRotation + _localRotation);
        worldScale = Vector2.Scale(parentScale, _localScale);
    }
    private void SetParentCore(Transform? newParent, bool worldPositionStays)
    {
        if (ReferenceEquals(newParent, this) || IsDescendantOf(newParent))
            throw new InvalidOperationException("A Transform cannot be parented to itself or one of its descendants.");
        var worldPosition = GetPositionCore();
        var worldRotation = GetRotationCore();
        var previousParent = _parent;
        _parent?._children.Remove(this);
        _parent = newParent;
        _parent?._children.Add(this);
        GameObjectUnchecked.SynchronizeActiveStateHierarchy();
        if (worldPositionStays)
        {
            SetPositionCore(worldPosition);
            SetRotationCore(worldRotation);
        }
        SceneRuntime.NotifyTransformParentChanged(this, previousParent, newParent);
    }
    private void SetSiblingIndexCore(int index)
    {
        if (_parent is null)
        {
            GameObjectUnchecked.SceneUnchecked?.SetRootSiblingIndexUnchecked(GameObjectUnchecked, index);
            return;
        }
        _parent._children.Remove(this);
        _parent._children.Insert(Math.Clamp(index, 0, _parent._children.Count), this);
    }
    private bool IsDescendantOf(Transform? candidate)
    {
        for (var current = candidate; current is not null; current = current._parent)
            if (ReferenceEquals(current, this)) return true;
        return false;
    }
    private static Vector2 DivideByScale(Vector2 value, Vector2 scale) => new(
        scale.x == Fix64.Zero ? Fix64.Zero : value.x / scale.x,
        scale.y == Fix64.Zero ? Fix64.Zero : value.y / scale.y);
    internal static Vector2 RotateVector(Vector2 value, Fix64 degrees)
    {
        if (NormalizeDegrees(degrees) == Fix64.Zero) return value;
        var radians = degrees * Fix64.Deg2Rad;
        var cosine = Fix64.Cos(radians);
        var sine = Fix64.Sin(radians);
        return new Vector2(value.x * cosine - value.y * sine,
            value.x * sine + value.y * cosine);
    }
    private static Fix64 NormalizeDegrees(Fix64 value)
    {
        value %= 360;
        if (value > 180) value -= 360;
        else if (value <= -180) value += 360;
        return value;
    }
}
