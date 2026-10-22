namespace BEngine.Navigation2D;

public sealed class NavigationPath2D
{
    private Vector2[] _corners = [];
    public Vector2[] corners { get => [.. _corners]; internal set => _corners = value ?? []; }
    /// <summary>Read-only view for runtime steering without copying corners.</summary>
    public ReadOnlySpan<Vector2> cornersSpan => _corners;
    internal Vector2[] CornersUnchecked => _corners;
    public NavigationPathStatus status { get; internal set; } = NavigationPathStatus.PathInvalid;
    public void ClearCorners()
    {
        corners = [];
        status = NavigationPathStatus.PathInvalid;
    }
}
