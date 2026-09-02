namespace BEngine.Editor;

internal readonly record struct EditorObjectPingSnapshot
{
    internal EditorObjectPingSnapshot(int instanceId, string? assetPath, double startedAt)
    {
        InstanceId = instanceId;
        AssetPath = assetPath ?? string.Empty;
        StartedAt = startedAt;
    }

    internal int InstanceId { get; }
    internal string AssetPath { get; }
    internal double StartedAt { get; }
}
