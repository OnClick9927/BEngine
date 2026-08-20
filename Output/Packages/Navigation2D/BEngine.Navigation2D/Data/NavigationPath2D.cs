namespace BEngine.Navigation2D;

public sealed class NavigationPath2D
{
    private Vector2[] _corners = [];
    public Vector2[] corners { get => [.. _corners]; internal set => _corners = value ?? []; }
    public NavigationPathStatus status { get; internal set; } = NavigationPathStatus.PathInvalid;
    public void ClearCorners()
    {
        _corners = [];
        status = NavigationPathStatus.PathInvalid;
    }
}
