using BEngine.Entities;

namespace BEngine;

public class Transform : Component
{
    private readonly List<Transform> _children = [];
    private readonly ThreadGuardedReadOnlyList<Transform> _childrenView;
    private Transform? _parent;
    private Vector2 _localPosition = Vector2.zero;
    private Fix64 _localRotation;
    private Vector2 _localScale = Vector2.one;

    public Transform() => _childrenView = new ThreadGuardedReadOnlyList<Transform>(_children);

    public Transform? parent
    {
        get { MainThreadGuard.Ensure(); return _parent; }
        private set => _parent = value;
    }
    public IReadOnlyList<Transform> children
    {
        get { MainThreadGuard.Ensure(); return _childrenView; }
    }
    public int childCount
    {
        get { MainThreadGuard.Ensure(); return _children.Count; }
    }
    public Transform root
    {
        get { MainThreadGuard.Ensure(); return RootUnchecked; }
    }

    internal Transform? ParentUnchecked => _parent;
    internal IReadOnlyList<Transform> ChildrenUnchecked => _children;
    internal Transform RootUnchecked => _parent?.RootUnchecked ?? this;
    internal Vector2 LocalPositionUnchecked => GetLocalPositionCore();
    internal Fix64 LocalRotationUnchecked => GetLocalRotationCore();
    internal Vector2 LocalScaleUnchecked => GetLocalScaleCore();

    public Vector2 localPosition
    {
        get { MainThreadGuard.Ensure(); return GetLocalPositionCore(); }
        set { MainThreadGuard.Ensure(); SetLocalPositionCore(value); }
    }
    public Fix64 localRotation
    {
        get { MainThreadGuard.Ensure(); return GetLocalRotationCore(); }
        set { MainThreadGuard.Ensure(); SetLocalRotationCore(value); }
    }
    public Vector2 localScale
    {
        get { MainThreadGuard.Ensure(); return GetLocalScaleCore(); }
        set { MainThreadGuard.Ensure(); SetLocalScaleCore(value); }
    }
    public Vector2 position
    {
        get { MainThreadGuard.Ensure(); return GetPositionCore(); }
        set { MainThreadGuard.Ensure(); SetPositionCore(value); }
    }
    public Fix64 rotation
    {
        get { MainThreadGuard.Ensure(); return GetRotationCore(); }
        set { MainThreadGuard.Ensure(); SetRotationCore(value); }
    }
    public Vector2 lossyScale
    {
        get { MainThreadGuard.Ensure(); return GetLossyScaleCore(); }
    }
    public Vector2 right
    {
        get { MainThreadGuard.Ensure(); return RotateVector(Vector2.right, GetRotationCore()); }
    }
    public Vector2 up
    {
        get { MainThreadGuard.Ensure(); return RotateVector(Vector2.up, GetRotationCore()); }
    }

    public Transform GetChild(int index)
    {
        MainThreadGuard.Ensure();
        return _children[index];
    }
    public void SetParent(Transform? newParent, bool worldPositionStays = true)
    {
        MainThreadGuard.Ensure();
        SetParentCore(newParent, worldPositionStays);
    }
    public void Translate(Vector2 translation, Space relativeTo = Space.Self)
    {
        MainThreadGuard.Ensure();
        var delta = relativeTo == Space.Self ? RotateVector(translation, GetRotationCore()) : translation;
        SetPositionCore(GetPositionCore() + delta);
    }
    public void Translate(Fix64 x, Fix64 y, Space relativeTo = Space.Self) =>
        Translate(new Vector2(x, y), relativeTo);
    public void Rotate(Fix64 degrees, Space relativeTo = Space.Self)
    {
        MainThreadGuard.Ensure();
        if (relativeTo == Space.World && _parent is not null) SetRotationCore(GetRotationCore() + degrees);
        else SetLocalRotationCore(GetLocalRotationCore() + degrees);
    }
    public void SetPositionAndRotation(Vector2 worldPosition, Fix64 worldRotation)
    {
        MainThreadGuard.Ensure();
        SetPositionCore(worldPosition);
        SetRotationCore(worldRotation);
    }
    public Vector2 TransformPoint(Vector2 point)
    {
        MainThreadGuard.Ensure();
        return GetPositionCore() + RotateVector(Vector2.Scale(GetLossyScaleCore(), point), GetRotationCore());
    }
    public Vector2 InverseTransformPoint(Vector2 point)
    {
        MainThreadGuard.Ensure();
        return DivideByScale(RotateVector(point - GetPositionCore(), -GetRotationCore()), GetLossyScaleCore());
    }
    public Vector2 TransformDirection(Vector2 direction)
    {
        MainThreadGuard.Ensure();
        return RotateVector(direction, GetRotationCore());
    }
    public Vector2 InverseTransformDirection(Vector2 direction)
    {
        MainThreadGuard.Ensure();
        return RotateVector(direction, -GetRotationCore());
    }
    public Vector2 TransformVector(Vector2 vector)
    {
        MainThreadGuard.Ensure();
        return RotateVector(Vector2.Scale(GetLossyScaleCore(), vector), GetRotationCore());
    }
    public Vector2 InverseTransformVector(Vector2 vector)
    {
        MainThreadGuard.Ensure();
        return DivideByScale(RotateVector(vector, -GetRotationCore()), GetLossyScaleCore());
    }
    public void DetachChildren()
    {
        MainThreadGuard.Ensure();
        foreach (var child in _children.ToArray()) child.SetParentCore(null, true);
    }
    public bool IsChildOf(Transform potentialParent)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(potentialParent);
        for (var current = _parent; current is not null; current = current._parent)
            if (ReferenceEquals(current, potentialParent)) return true;
        return false;
    }
    public int GetSiblingIndex()
    {
        MainThreadGuard.Ensure();
        return _parent?._children.IndexOf(this) ??
            GameObjectUnchecked.SceneUnchecked?.RootIndexOfUnchecked(GameObjectUnchecked) ?? 0;
    }
    public void SetSiblingIndex(int index)
    {
        MainThreadGuard.Ensure();
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
    internal void CaptureEntityState()
    {
        if (!TryGetEntityTransform(out var transform)) return;
        _localPosition = transform.Position;
        _localRotation = transform.Rotation;
        _localScale = transform.Scale;
    }

    private Vector2 GetLocalPositionCore() =>
        TryGetEntityTransform(out var data) ? data.Position : _localPosition;
    private Fix64 GetLocalRotationCore() =>
        TryGetEntityTransform(out var data) ? data.Rotation : _localRotation;
    private Vector2 GetLocalScaleCore() =>
        TryGetEntityTransform(out var data) ? data.Scale : _localScale;
    private void SetLocalPositionCore(Vector2 value)
    {
        _localPosition = value;
        if (TryGetEntityTransformReference(out var manager))
            manager.GetComponentDataRW<LocalTransform>(GameObjectUnchecked.EntityUnchecked).Position = value;
    }
    private void SetLocalRotationCore(Fix64 value)
    {
        _localRotation = NormalizeDegrees(value);
        if (TryGetEntityTransformReference(out var manager))
            manager.GetComponentDataRW<LocalTransform>(GameObjectUnchecked.EntityUnchecked).Rotation = _localRotation;
    }
    private void SetLocalScaleCore(Vector2 value)
    {
        _localScale = value;
        if (TryGetEntityTransformReference(out var manager))
            manager.GetComponentDataRW<LocalTransform>(GameObjectUnchecked.EntityUnchecked).Scale = value;
    }
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
    private bool TryGetEntityTransform(out LocalTransform transform)
    {
        var owner = GameObjectUnchecked;
        if (owner.SceneUnchecked is { } scene)
            return scene.WorldUnchecked.EntityManager.TryGetComponentData(owner.EntityUnchecked, out transform);
        transform = default;
        return false;
    }
    private bool TryGetEntityTransformReference(out EntityManager manager)
    {
        var owner = GameObjectUnchecked;
        if (owner.SceneUnchecked is { } scene)
        {
            manager = scene.WorldUnchecked.EntityManager;
            return manager.HasComponent<LocalTransform>(owner.EntityUnchecked);
        }
        manager = null!;
        return false;
    }
    private static Vector2 DivideByScale(Vector2 value, Vector2 scale) => new(
        scale.x == Fix64.Zero ? Fix64.Zero : value.x / scale.x,
        scale.y == Fix64.Zero ? Fix64.Zero : value.y / scale.y);
    internal static Vector2 RotateVector(Vector2 value, Fix64 degrees)
    {
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
