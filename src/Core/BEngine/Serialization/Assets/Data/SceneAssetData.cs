using YamlDotNet.Serialization;

namespace BEngine.Serialization;

internal sealed class SceneAssetData
{
    public string Format { get; set; } = "BEngine.Scene";
    public int Version { get; set; } = 2;
    public Guid Id { get; set; }
    public string Name { get; set; } = "Untitled";
    public List<GameObjectData> GameObjects { get; set; } = [];

    [YamlIgnore]
    internal bool IsRuntimeSnapshot { get; set; }
}
