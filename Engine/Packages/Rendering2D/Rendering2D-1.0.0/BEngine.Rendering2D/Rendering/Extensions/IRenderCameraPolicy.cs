namespace BEngine.Rendering;

/// <summary>Optional camera projection and world alignment supplied by a component.</summary>
public interface IRenderCameraPolicy
{
    RenderCamera ForViewport(RenderCamera camera, int width, int height);
    Vector2 SnapWorldPoint(Vector2 point);
    bool AlignViewportCenter { get; }
}

/// <summary>Opts an immutable camera policy into reuse of derived projection data.</summary>
/// <remarks>
/// All policy members must be pure and depend only on their arguments and immutable
/// instance values. For the lifetime of this instance, equal arguments must produce
/// equal results; AlignViewportCenter must always return the same value without
/// throwing. Calls may be omitted or their results reused during rendering, so an
/// implementation must not rely on call counts, ordering or observable side effects.
/// Create a new instance when any input changes. This contract does not by itself
/// permit reuse across different policy instances or different camera/viewport inputs.
/// </remarks>
public interface IStableRenderCameraPolicy : IRenderCameraPolicy { }

public interface IRenderCameraPolicyProvider
{
    IRenderCameraPolicy CreatePolicy();
}
