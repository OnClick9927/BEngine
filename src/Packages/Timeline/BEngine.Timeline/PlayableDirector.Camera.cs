namespace BEngine;

public sealed partial class PlayableDirector
{
    private readonly Dictionary<Camera2D, CameraState> _cameraDefaults = new(ReferenceEqualityComparer.Instance);

    private void RestoreCameraDefaults()
    {
        foreach (var (camera, state) in _cameraDefaults)
            if (!camera.isDestroyed && !camera.gameObject.isDestroyed) state.Restore(camera);
    }

    private void EvaluateCamera(Camera2D output, TimelineClip[] clips)
    {
        var shots = clips.Select(clip => (Clip: clip, Source: GetGenericBinding(clip.cameraBinding)?.GetComponent<Camera2D>()))
            .Where(item => item.Source is { isDestroyed: false } && !ReferenceEquals(item.Source, output)).ToArray();
        if (shots.Length == 0) return;
        _cameraDefaults.TryAdd(output, CameraState.Capture(output));
        var weights = shots.Select(item => AnimationWeight(item.Clip, shots.Select(shot => shot.Clip), _time)).ToArray();
        var sum = weights.Aggregate(Fix64.Zero, (total, value) => total + value);
        if (sum <= 0) { weights[^1] = 1; sum = 1; }
        var position = Vector2.zero; var rotation = Fix64.Zero; var size = Fix64.Zero;
        var color = new Color(0, 0, 0, 0);
        var referenceAngle = shots[0].Source!.transform.rotation;
        var strongest = 0;
        for (var index = 0; index < shots.Length; index++)
        {
            var camera = shots[index].Source!; var weight = weights[index] / sum;
            position += camera.transform.position * weight;
            var delta = Mathf.Repeat(camera.transform.rotation - referenceAngle + 180, 360) - 180;
            rotation += (referenceAngle + delta) * weight;
            size += camera.size * weight;
            color = new(color.r + camera.backgroundColor.r * weight, color.g + camera.backgroundColor.g * weight,
                color.b + camera.backgroundColor.b * weight, color.a + camera.backgroundColor.a * weight);
            if (weights[index] >= weights[strongest]) strongest = index;
        }
        var dominant = shots[strongest].Source!;
        output.transform.position = position; output.transform.rotation = rotation;
        output.size = size; output.backgroundColor = color;
        output.clearMode = dominant.clearMode; output.cullingMask = dominant.cullingMask;
        output.viewportRect = dominant.viewportRect;
    }

    private sealed record CameraState(Vector2 Position, Fix64 Rotation, Fix64 Size, Color Background,
        CameraClearMode ClearMode, ulong CullingMask, Rect Viewport)
    {
        internal static CameraState Capture(Camera2D camera) => new(camera.transform.localPosition,
            camera.transform.localRotation, camera.size, camera.backgroundColor, camera.clearMode,
            camera.cullingMask, camera.viewportRect);
        internal void Restore(Camera2D camera)
        {
            camera.transform.localPosition = Position; camera.transform.localRotation = Rotation;
            camera.size = Size; camera.backgroundColor = Background; camera.clearMode = ClearMode;
            camera.cullingMask = CullingMask; camera.viewportRect = Viewport;
        }
    }
}
