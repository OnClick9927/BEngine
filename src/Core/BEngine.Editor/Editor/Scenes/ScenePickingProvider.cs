using BEngine.Rendering;

namespace BEngine.Editor;

public delegate long ScenePickingProvider(
    Scene scene,
    RenderCamera camera,
    Vector2 viewportPoint,
    int viewportWidth,
    int viewportHeight,
    ICollection<ScenePickCandidate> candidates,
    long submissionOrder,
    Predicate<GameObject>? objectFilter);
