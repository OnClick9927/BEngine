using BEngine;

namespace Game;

public sealed class PlayerMover : MonoBehaviour
{
    public Fix64 speed { get; set; } = Fix64.Parse("3.2");
    public Vector2 moveBounds { get; set; } = new(Fix64.Parse("7.2"), Fix64.Parse("3.6"));

    public override void Update()
    {
        var horizontal = Axis(KeyCode.A, KeyCode.D) + Axis(KeyCode.LeftArrow, KeyCode.RightArrow);
        var vertical = Axis(KeyCode.S, KeyCode.W) + Axis(KeyCode.DownArrow, KeyCode.UpArrow);
        var direction = new Vector2(
            Fix64.Clamp(horizontal, -Fix64.One, Fix64.One),
            Fix64.Clamp(vertical, -Fix64.One, Fix64.One));
        if (direction.sqrMagnitude > Fix64.One) direction = direction.normalized;
        var next = transform.localPosition + direction * speed * Time.deltaTime;
        transform.localPosition = new Vector2(
            Fix64.Clamp(next.x, -Fix64.Abs(moveBounds.x), Fix64.Abs(moveBounds.x)),
            Fix64.Clamp(next.y, -Fix64.Abs(moveBounds.y), Fix64.Abs(moveBounds.y)));
    }

    public override void Reset()
    {
        speed = Fix64.Parse("3.2");
        moveBounds = new Vector2(Fix64.Parse("7.2"), Fix64.Parse("3.6"));
        runInEditMode = false;
    }

    private static Fix64 Axis(KeyCode negative, KeyCode positive) =>
        (Input.GetKey(positive) ? Fix64.One : Fix64.Zero) -
        (Input.GetKey(negative) ? Fix64.One : Fix64.Zero);
}
