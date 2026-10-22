namespace BEngine;

public class Transform : Component
{
    private readonly List<Transform> _children = [];
    private readonly IReadOnlyList<Transform> _childrenView;
    private Transform? _parent;
    private Vector2 _localPosition = Vector2.zero;
    private Fix64 _localRotation;
    private Vector2 _localScale = Vector2.one;
    private bool _worldDirty = true;
    private Vector2 _worldPosition, _worldScale;
    private Fix64 _worldRotation;
    private AffineTransform2D _worldMatrix, _inverseMatrix;
    private AffineTransform2D _localMatrix;
    private bool _localMatrixDirty = true, _inverseDirty = true;
    private long _worldVersion;
    private static readonly List<Transform> PoseChain = [];
    private static readonly Stack<Transform> DirtyChain = new();

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
    internal Transform RootUnchecked { get { var current = this; while (current._parent is not null) current = current._parent; return current; } }
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
    public AffineTransform2D localToWorldMatrix { get { EnsureWorldPose(); return _worldMatrix; } }
    /// <summary>Uses a minimum-norm inverse projection when a scale axis is zero.</summary>
    public AffineTransform2D worldToLocalMatrix
    {
        get
        {
            EnsureWorldPose();
            if (_inverseDirty) { _inverseMatrix = _worldMatrix.InverseOrProjection(); _inverseDirty = false; }
            return _inverseMatrix;
        }
    }
    public long worldVersion { get { EnsureWorldPose(); return _worldVersion; } }
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
        return localToWorldMatrix.TransformPoint(point);
    }
    public Vector2 InverseTransformPoint(Vector2 point)
    {
        return worldToLocalMatrix.TransformPoint(point);
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
        return localToWorldMatrix.TransformVector(vector);
    }
    public Vector2 InverseTransformVector(Vector2 vector)
    {
        return worldToLocalMatrix.TransformVector(vector);
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
        replacement._localMatrixDirty = true;
        if (_parent is not null)
        {
            var index = _parent._children.IndexOf(this);
            if (index >= 0) _parent._children[index] = replacement;
            replacement._parent = _parent;
        }
        foreach (var child in _children)
        {
            child._parent = replacement;
            child.MarkWorldDirty();
            replacement._children.Add(child);
        }
        _parent = null;
        MarkWorldDirty(); replacement.MarkWorldDirty();
        _children.Clear();
    }
    private Vector2 GetLocalPositionCore() => _localPosition;
    private Fix64 GetLocalRotationCore() => _localRotation;
    private Vector2 GetLocalScaleCore() => _localScale;
    private void SetLocalPositionCore(Vector2 value) { if (_localPosition == value) return; _localPosition = value; MarkWorldDirty(); }
    private void SetLocalRotationCore(Fix64 value)
    {
        var normalized = NormalizeDegrees(value); if (_localRotation == normalized) return;
        _localRotation = normalized; _localMatrixDirty = true; MarkWorldDirty();
    }
    private void SetLocalScaleCore(Vector2 value) { if (_localScale == value) return; _localScale = value; _localMatrixDirty = true; MarkWorldDirty(); }
    private Vector2 GetPositionCore() { GetWorldPoseCore(out var position, out _, out _); return position; }
    private void SetPositionCore(Vector2 value)
    {
        if (_parent is null) SetLocalPositionCore(value);
        else SetLocalPositionCore(_parent.InverseTransformPoint(value));
    }
    private Fix64 GetRotationCore() { GetWorldPoseCore(out _, out var rotation, out _); return rotation; }
    private void SetRotationCore(Fix64 value) => SetLocalRotationCore(_parent is null
        ? value
        : value - _parent.GetRotationCore());
    private Vector2 GetLossyScaleCore() { GetWorldPoseCore(out _, out _, out var scale); return scale; }
    private void EnsureWorldPose() => GetWorldPoseCore(out _, out _, out _);
    private void GetWorldPoseCore(out Vector2 worldPosition, out Fix64 worldRotation, out Vector2 worldScale)
    {
        if (_worldDirty)
        {
            try
            {
                for (var current = this; current is not null && current._worldDirty; current = current._parent) PoseChain.Add(current);
                for (var index = PoseChain.Count - 1; index >= 0; index--)
                {
                    PoseChain[index].RebuildWorldPose();
                }
            }
            finally { PoseChain.Clear(); }
        }
        worldPosition = _worldPosition; worldRotation = _worldRotation; worldScale = _worldScale;
    }
    // Caller supplies a parent-first batch. Ordinary property reads retain lazy chain evaluation.
    internal void SynchronizeWorldPoseUnchecked() { if (_worldDirty) RebuildWorldPose(); }
    private void RebuildWorldPose()
    {
        if (_localMatrixDirty)
        { _localMatrix = AffineTransform2D.TRS(_localPosition, _localRotation, _localScale); _localMatrixDirty = false; }
        var local = _localMatrix with { Tx = _localPosition.x, Ty = _localPosition.y };
        if (_parent is not { } ancestor)
        { _worldMatrix = local; _worldRotation = _localRotation; _worldScale = _localScale; }
        else
        {
            var previous = _worldMatrix;
            _worldMatrix = ancestor._worldMatrix * local;
            _worldRotation = NormalizeDegrees(ancestor._worldRotation + _localRotation);
            var signs = Vector2.Scale(ancestor._worldScale, _localScale);
            if (previous.M11 != _worldMatrix.M11 || previous.M21 != _worldMatrix.M21 ||
                (_worldScale.x < 0) != (signs.x < 0))
                _worldScale = new(Fix64.Hypot(_worldMatrix.M11, _worldMatrix.M21) * (signs.x < 0 ? -1 : 1), _worldScale.y);
            if (previous.M12 != _worldMatrix.M12 || previous.M22 != _worldMatrix.M22 ||
                (_worldScale.y < 0) != (signs.y < 0))
                _worldScale = new(_worldScale.x, Fix64.Hypot(_worldMatrix.M12, _worldMatrix.M22) * (signs.y < 0 ? -1 : 1));
        }
        _worldPosition = _worldMatrix.translation; _inverseDirty = true;
        _worldVersion++; _worldDirty = false;
    }
    private void MarkWorldDirty()
    {
        if (_worldDirty) return;
        DirtyChain.Push(this);
        try
        {
            while (DirtyChain.TryPop(out var current))
            {
                if (current._worldDirty) continue; current._worldDirty = true;
                foreach (var child in current._children) if (!child._worldDirty) DirtyChain.Push(child);
            }
        }
        finally { DirtyChain.Clear(); }
    }
    private void SetParentCore(Transform? newParent, bool worldPositionStays)
    {
        if (ReferenceEquals(newParent, this) || IsDescendantOf(newParent))
            throw new InvalidOperationException("A Transform cannot be parented to itself or one of its descendants.");
        if (newParent is not null &&
            !ReferenceEquals(GameObjectUnchecked.SceneUnchecked, newParent.GameObjectUnchecked.SceneUnchecked))
            throw new InvalidOperationException("Transforms in different Scenes cannot share a hierarchy.");

        var worldPosition = Vector2.zero;
        var worldRotation = Fix64.Zero;
        var worldScale = Vector2.one;
        if (worldPositionStays)
            GetWorldPoseCore(out worldPosition, out worldRotation, out worldScale);
        var previousParent = _parent;
        _parent?._children.Remove(this);
        _parent = newParent;
        MarkWorldDirty();
        _parent?._children.Add(this);
        GameObjectUnchecked.SceneUnchecked?.InvalidateHierarchy();
        GameObjectUnchecked.SynchronizeActiveStateHierarchy();
        if (worldPositionStays)
        {
            SetPositionCore(worldPosition);
            SetRotationCore(worldRotation);
            SetLossyScaleCore(worldScale);
        }
        SceneRuntime.NotifyTransformParentChanged(this, previousParent, newParent);
    }
    private void SetLossyScaleCore(Vector2 value)
    {
        if (_parent is null) { SetLocalScaleCore(value); return; }
        var x = _parent.TransformVector(RotateVector(Vector2.right, _localRotation));
        var y = _parent.TransformVector(RotateVector(Vector2.up, _localRotation));
        var signs = _parent.lossyScale;
        SetLocalScaleCore(DivideByScale(value, new Vector2(
            Fix64.Hypot(x.x, x.y) * (signs.x < 0 ? -1 : 1),
            Fix64.Hypot(y.x, y.y) * (signs.y < 0 ? -1 : 1))));
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
        GameObjectUnchecked.SceneUnchecked?.InvalidateHierarchy();
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
        degrees = NormalizeDegrees(degrees);
        if (degrees == 0) return value;
        if (degrees == 90) return new(-value.y, value.x);
        if (degrees == -90) return new(value.y, -value.x);
        if (degrees == 180) return -value;
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
