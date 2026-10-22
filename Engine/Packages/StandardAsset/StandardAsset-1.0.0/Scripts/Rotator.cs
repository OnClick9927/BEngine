using BEngine;

namespace StandardAsset;

[AddComponentMenu("Standard Asset/Rotator")]
public sealed class Rotator : MonoBehaviour
{
    public Fix64 degreesPerSecond { get; set; } = 90;
    public override void Update() => transform.rotation += degreesPerSecond * Time.deltaTime;
}
