namespace BEngine.Navigation2D;

[AddComponentMenu("Navigation 2D/Navigation Link 2D")]
public sealed class NavigationLink2D : Behaviour
{
    public Vector2 startPoint { get; set; } = Vector2.left;
    public Vector2 endPoint { get; set; } = Vector2.right;
    public Fix64 width { get; set; }
    public Fix64 costModifier { get; set; } = -1;
    public bool bidirectional { get; set; } = true;
    public int area { get; set; }
    public Vector2 worldStart => transform.TransformPoint(startPoint);
    public Vector2 worldEnd => transform.TransformPoint(endPoint);
}
