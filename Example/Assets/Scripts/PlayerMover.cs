using BEngine;

namespace Game;

public sealed class PlayerMover : MonoBehaviour
{
    public Fix64 speed { get; set; } = 3;

    public override void Update()
    {
        var horizontal = Axis(KeyCode.A, KeyCode.D) + Axis(KeyCode.LeftArrow, KeyCode.RightArrow);
        var vertical = Axis(KeyCode.S, KeyCode.W) + Axis(KeyCode.DownArrow, KeyCode.UpArrow);
        var direction = new Vector2(
            Fix64.Clamp(horizontal, -Fix64.One, Fix64.One),
            Fix64.Clamp(vertical, -Fix64.One, Fix64.One));
        if (direction.sqrMagnitude > Fix64.One) direction = direction.normalized;
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
    }

    private static Fix64 Axis(KeyCode negative, KeyCode positive) =>
        (Input.GetKey(positive) ? Fix64.One : Fix64.Zero) -
        (Input.GetKey(negative) ? Fix64.One : Fix64.Zero);
}
