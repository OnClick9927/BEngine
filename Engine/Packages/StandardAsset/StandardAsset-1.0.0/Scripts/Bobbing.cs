using BEngine;

namespace StandardAsset;

[AddComponentMenu("Standard Asset/Bobbing")]
public sealed class Bobbing : MonoBehaviour
{
    public Vector2 amplitude { get; set; } = Vector2.up;
    public Fix64 frequency { get; set; } = 1;
    [System.NonSerialized] private Vector2 _origin;
    [System.NonSerialized] private Fix64 _elapsed;
    public override void Start() => _origin = transform.localPosition;
    public override void Update()
    {
        _elapsed += Time.deltaTime;
        transform.localPosition = _origin + amplitude * Mathf.Sin(_elapsed * frequency * Mathf.PI * 2);
    }
}
