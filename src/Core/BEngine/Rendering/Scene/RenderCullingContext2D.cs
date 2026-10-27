namespace BEngine.Rendering;

/// <summary>Reusable camera bounds. Preserves the fixed-point IsVisible boundary semantics.</summary>
public sealed class RenderCullingContext2D
{
    private static readonly Fix64 MinimumHalfHeight = Fix64.Parse("0.001");
    private RenderCamera _camera;
    private Fix64 _halfWidth, _halfHeight, _cosine, _sine;
    private bool _aligned;
    private Fix64 _lastRotation, _extentCosine, _extentSine;
    private bool _hasRotation;
    private byte _extentAxes;

    public RenderCullingContext2D(RenderCamera camera, int width, int height)
        => Reset(camera, width, height);

    /// <summary>Reuses the query object; invalidates its last rotation and recomputes camera bounds.</summary>
    public void Reset(RenderCamera camera, int width, int height)
    {
        _hasRotation = false;
        _camera = camera;
        _halfHeight = Fix64.Max(MinimumHalfHeight, camera.Size);
        _halfWidth = _halfHeight * Math.Max(1, width) / Math.Max(1, height);
        _aligned = camera.Rotation % 360 == 0;
        var radians = -camera.Rotation * Fix64.Deg2Rad;
        _cosine = Fix64.Cos(radians); _sine = Fix64.Sin(radians);
    }

    public bool IsVisible(Vector2 center, Fix64 rotation, Vector2 size)
    {
        var absolute = new Vector2(Fix64.Abs(size.x), Fix64.Abs(size.y));
        // Multiplication by Q32.32 one half floors each raw integer. Abs and
        // its checked exceptional order remain identical to the general path.
        var half = new Vector2(Fix64.FromRaw(absolute.x.RawValue >> 1), Fix64.FromRaw(absolute.y.RawValue >> 1));
        var local = center - _camera.Position;
        if (!_aligned) local = new(local.x * _cosine - local.y * _sine, local.x * _sine + local.y * _cosine);
        if (!_hasRotation || rotation != _lastRotation)
        {
            var relative = (rotation - _camera.Rotation) * Fix64.Deg2Rad;
            _extentCosine = Fix64.Abs(Fix64.Cos(relative)); _extentSine = Fix64.Abs(Fix64.Sin(relative));
            _extentAxes = _extentCosine == Fix64.One && _extentSine == Fix64.Zero ? (byte)1
                : _extentCosine == Fix64.Zero && _extentSine == Fix64.One ? (byte)2 : (byte)0;
            _lastRotation = rotation; _hasRotation = true;
        }
        Fix64 x, y;
        if (_extentAxes == 1) { x = half.x; y = half.y; }
        else if (_extentAxes == 2) { x = half.y; y = half.x; }
        else
        {
            x = _extentCosine * half.x + _extentSine * half.y;
            y = _extentSine * half.x + _extentCosine * half.y;
        }
        return local.x + x >= -_halfWidth && local.x - x <= _halfWidth &&
               local.y + y >= -_halfHeight && local.y - y <= _halfHeight;
    }
    public bool IsVisible(SceneQuad2D quad) => IsVisibleCore(in quad);

    internal bool IsVisibleCore(in SceneQuad2D quad)
    {
        if (quad.WorldAxisX is null || quad.WorldAxisY is null) return IsVisible(quad.Center, quad.Rotation, quad.Size);
        quad.GetWorldAxes(out var x, out var y);
        var local = quad.Center - _camera.Position;
        if (!_aligned) { local = Rotate(local); x = Rotate(x); y = Rotate(y); }
        var extentX = Fix64.Abs(x.x) + Fix64.Abs(y.x);
        var extentY = Fix64.Abs(x.y) + Fix64.Abs(y.y);
        return local.x + extentX >= -_halfWidth && local.x - extentX <= _halfWidth &&
               local.y + extentY >= -_halfHeight && local.y - extentY <= _halfHeight;
    }
    private Vector2 Rotate(Vector2 v) => new(v.x * _cosine - v.y * _sine, v.x * _sine + v.y * _cosine);
}
