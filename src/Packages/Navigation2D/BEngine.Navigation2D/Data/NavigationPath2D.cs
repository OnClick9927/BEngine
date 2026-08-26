namespace BEngine.Navigation2D;

public sealed class NavigationPath2D
{
    public Vector2[] corners { get => [.. field]; internal set => field = value ?? []; } = [];
    public NavigationPathStatus status { get; internal set; } = NavigationPathStatus.PathInvalid;
    public void ClearCorners()
    {
        corners = [];
        status = NavigationPathStatus.PathInvalid;
    }
}
