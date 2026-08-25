using BEngine;

namespace Game;

public sealed class ShowcaseMotion : MonoBehaviour
{
    public Vector2 movementAmplitude { get; set; } = Vector2.zero;
    public Fix64 movementSpeed { get; set; } = 1;
    public Fix64 rotationSpeed { get; set; }
    public Fix64 phaseOffset { get; set; }

    private Vector2 _origin;

    public override void Start()
    {
        _origin = transform.localPosition;
    }

    public override void Update()
    {
        var wave = Mathf.Sin(Time.time * movementSpeed + phaseOffset);
        transform.localPosition = _origin + movementAmplitude * wave;
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}
