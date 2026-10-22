using BEngine;

namespace StandardAsset;

[AddComponentMenu("Standard Asset/Top Down Mover")]
public sealed class TopDownMover : MonoBehaviour
{
    public Fix64 speed { get; set; } = 4;
    public bool readKeyboard { get; set; } = true;
    public Vector2 moveInput { get; set; }

    public override void Update()
    {
        var direction = readKeyboard
            ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) : moveInput;
        Move(direction, Time.deltaTime);
    }

    public void Move(Vector2 direction, Fix64 deltaTime)
    {
        if (deltaTime < 0) throw new System.ArgumentOutOfRangeException(nameof(deltaTime));
        if (direction.sqrMagnitude > 1) direction = direction.normalized;
        transform.position += direction * Fix64.Max(0, speed) * deltaTime;
    }
}
