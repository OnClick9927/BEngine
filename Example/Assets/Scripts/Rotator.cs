using BEngine;

namespace Game;

public sealed class Rotator : MonoBehaviour
{
    public Fix64 degreesPerSecond { get; set; } = 45;

    public override void Update()
    {
        transform.localRotation += degreesPerSecond * Time.deltaTime;
    }
}
