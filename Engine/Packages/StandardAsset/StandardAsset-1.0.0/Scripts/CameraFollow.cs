using BEngine;

namespace StandardAsset;

[AddComponentMenu("Standard Asset/Camera Follow")]
public sealed class CameraFollow : MonoBehaviour
{
    public Transform? target { get; set; }
    public Vector2 offset { get; set; }
    public Fix64 damping { get; set; } = Fix64.Parse("0.3");
    public override void LateUpdate()
    {
        if (target is null) return;
        var t = damping <= 0 ? Fix64.One : Time.deltaTime / (damping + Time.deltaTime);
        transform.position = Vector2.Lerp(transform.position, target.position + offset, t);
    }
    public void Snap() { if (target is not null) transform.position = target.position + offset; }
}
