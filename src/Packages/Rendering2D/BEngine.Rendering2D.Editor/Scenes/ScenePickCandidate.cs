using BEngine.Rendering;

namespace BEngine.Editor;

[RuntimeTypeAlias("BEngine.Editor.ScenePickCandidate, BEngine.EditorWorkbench.Editor")]
[RuntimeTypeAlias("BEngine.Editor.ScenePickCandidate, BEngine.EditorWorkbench.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public readonly record struct ScenePickCandidate(GameObject GameObject, RenderOrderKey SortKey);
