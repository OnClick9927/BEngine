namespace BEngine.Rendering;

/// <summary>Optional camera projection and world alignment supplied by a component.</summary>
public interface IRenderCameraPolicy
{
    RenderCamera ForViewport(RenderCamera camera, int width, int height);
    Vector2 SnapWorldPoint(Vector2 point);
    bool AlignViewportCenter { get; }
}

public interface IRenderCameraPolicyProvider
{
    IRenderCameraPolicy CreatePolicy();
}
