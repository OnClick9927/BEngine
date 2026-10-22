using BEngine.Rendering;

namespace BEngine.Editor;

public readonly record struct ScenePickCandidate(GameObject GameObject, RenderSortKey2D SortKey);
