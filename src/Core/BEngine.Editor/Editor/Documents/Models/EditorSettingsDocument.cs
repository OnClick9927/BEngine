using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class EditorSettingsDocument : Document
{
    public string Format { get; set; } = "BEngine.EditorSettings";
    public int Version { get; set; } = 1;
    public string Layout { get; set; } = "Unity";
    public string LastScene { get; set; } = "Assets/Scenes/Main.scene.yaml";
}
