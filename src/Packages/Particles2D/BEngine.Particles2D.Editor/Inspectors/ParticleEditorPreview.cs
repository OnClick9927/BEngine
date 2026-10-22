namespace BEngine.Editor;

internal sealed class ParticleEditorPreview : IEditorPreview
{
    private IReadOnlyList<ParticleSystem2D> _systems = [];
    internal void Sample(ParticleSystem2D system, Fix64 time)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Preview particles");
        var systems = system.PreviewSystems();
        foreach (var previous in _systems)
            if (!previous.isDestroyed && !systems.Contains(previous))
            { previous.ResetSimulation(); previous.editorPreview = false; }
        _systems = systems;
        EditorPreviewRegistry.Register(this);
        try { system.Simulate(time); system.editorPreview = true; system.editorPreviewTime = time; SceneView.RepaintAll(); }
        catch { Dispose(); throw; }
    }
    public void PrepareInspectorChanges(IReadOnlyCollection<UndoPropertyModification> modifications)
    {
        if (modifications.Count > 0) Dispose();
    }
    public void Dispose()
    {
        EditorPreviewRegistry.Unregister(this);
        foreach (var system in _systems)
            if (!system.isDestroyed) { system.ResetSimulation(); system.editorPreview = false; }
        _systems = []; SceneView.RepaintAll();
    }
}
