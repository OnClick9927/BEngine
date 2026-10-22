namespace BEngine.PostProcessing;

[RequireComponent(typeof(Camera2D))]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Post-process Layer")]
public sealed class PostProcessLayer : Behaviour
{
    public Transform? volumeTrigger { get; set; }
    public LayerMask volumeLayer { get; set; } = new(ulong.MaxValue);
    public bool stopNaNPropagation { get; set; } = true;
}
