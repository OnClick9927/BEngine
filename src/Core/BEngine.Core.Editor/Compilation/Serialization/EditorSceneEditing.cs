namespace BEngine.Editor;

/// <summary>Scene membership and lifetime operations for editor hosts, without
/// selection, history, dirty flags, asset import or workspace notifications.</summary>
public static class EditorSceneEditing
{
    /// <summary>Creates an empty unloaded reference without reading a document.
    /// The caller owns this scene and must close it when replacing the reference.</summary>
    public static Scene CreateUnloadedScene(string name, string path, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(path);
        return new Scene(name, services) { path = path, isLoaded = false };
    }

    /// <summary>Attaches an entirely detached root hierarchy, preserving identities
    /// and order. Existing scene membership is rejected before any node is bound.
    /// Destroyed editor objects may be restored; values are not deserialized.</summary>
    public static void AttachHierarchy(Scene destination, GameObject root)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(root);
        if (root.transform.parent is not null)
            throw new ArgumentException("Only a root hierarchy can be attached to a Scene.", nameof(root));
        destination.AddHierarchy(root);
    }

    /// <summary>Marks a scene unloaded and disposes its objects, runtime and service
    /// scope through the existing scene lifetime. Repeated closure is harmless.</summary>
    public static void CloseScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.isLoaded = false;
        scene.Dispose();
    }
}
