namespace BEngine.Editor;

/// <summary>Kernel operations for an authoring host. Window, transition, pause,
/// history and clock-snapshot policies belong to the host. Call on its engine thread.</summary>
public static class EditorHostRuntime
{
    /// <summary>Establishes edit mode and the host-selected assets address without
    /// creating files, normalizing paths, changing identity or resetting the clock.</summary>
    public static void ConfigureApplication(string assetsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsDirectory);
        RequireBetweenFrames();
        if (Application.isEditor && Application.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before configuring an editor host.");
        Application.isEditor = true;
        Application.isPlaying = false;
        Application.dataPath = assetsDirectory;
    }

    /// <summary>Starts a fresh simulation clock between frames, retaining time scale,
    /// fixed-step settings and the monotonic dispatch epoch. Does not enter Play Mode.
    /// Capture Time.State first if the host needs to restore its edit clock.</summary>
    public static void PreparePlayClock()
    {
        RequireEditMode();
        Time.Reset();
    }

    /// <summary>Publishes the host's completed scene preparation as Play Mode.
    /// Does not start scenes, initialize code, capture state or emit editor events.</summary>
    public static void EnterPlayMode()
    {
        RequireEditor();
        RequireBetweenFrames();
        Application.isPlaying = true;
    }

    /// <summary>Publishes edit mode after the host has stopped its runtimes.
    /// Idempotent; does not restore clock, rendering, input or workspace state.</summary>
    public static void ReturnToEditMode()
    {
        RequireEditor();
        RequireBetweenFrames();
        Application.isPlaying = false;
    }

    /// <summary>Advances only the edit clock. Negative elapsed time is clamped by
    /// the existing clock; no fixed-step simulation, task pump or input cleanup runs.</summary>
    public static void AdvanceEditFrame(Fix64 elapsed)
    {
        RequireEditMode();
        Time.AdvanceFrameUnchecked(elapsed);
    }

    /// <summary>Ends input consumption between runtime frames. Hosts call this once
    /// per native update (including paused updates), and after a manual simulation step.
    /// Call in a finally path when earlier host work fails.</summary>
    public static void CompleteInputFrame()
    {
        RequireEditor();
        RequireBetweenFrames();
        Input.EndFrame();
    }

    private static void RequireEditMode()
    {
        RequireEditor();
        RequireBetweenFrames();
        if (Application.isPlaying)
            throw new InvalidOperationException("An edit clock cannot advance or reset during Play Mode.");
    }

    private static void RequireEditor()
    {
        if (!Application.isEditor)
            throw new InvalidOperationException("Configure an editor host before using its runtime operations.");
    }

    private static void RequireBetweenFrames()
    {
        if (Time.isFrameActive || Time.inFixedTimeStep)
            throw new InvalidOperationException("Editor host state changes require a runtime frame boundary.");
    }
}
