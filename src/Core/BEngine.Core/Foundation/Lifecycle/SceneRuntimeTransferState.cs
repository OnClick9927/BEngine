namespace BEngine;

internal readonly record struct SceneRuntimeTransferState(
    bool Awakened,
    bool Enabled,
    bool Started);
