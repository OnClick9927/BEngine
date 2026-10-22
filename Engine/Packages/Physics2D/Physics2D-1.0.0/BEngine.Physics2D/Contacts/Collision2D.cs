namespace BEngine.Physics2D;

public sealed class Collision2D
{
    public Collider2D collider { get; internal init; } = null!;
    public GameObject gameObject => collider.gameObject;
    public Rigidbody2D? rigidbody => collider.attachedRigidbody;
    public Vector2 relativeVelocity { get; internal init; }
    public IReadOnlyList<ContactPoint2D> contacts { get; internal init; } = [];
    public int contactCount => contacts.Count;
    public Transform transform => collider.transform;
    public ContactPoint2D GetContact(int index) => contacts[index];
}
