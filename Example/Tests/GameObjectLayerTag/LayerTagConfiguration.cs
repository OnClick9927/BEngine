namespace BEngine.ExampleTests.GameObjectLayerTag;

internal sealed class LayerTagConfiguration : IDisposable
{
    private readonly string[] _originalTags;
    private readonly SortingLayerDefinition[] _originalLayers;

    private LayerTagConfiguration()
    {
        _originalTags = TagManager.tags.ToArray();
        _originalLayers = LayerMask.layers.ToArray();
    }

    public static LayerTagConfiguration ApplyTestConfiguration()
    {
        var configuration = new LayerTagConfiguration();
        TagManager.Configure([" Player ", "Enemy", "Player", string.Empty, "EditorOnly"]);
        SortingLayerRegistry.Configure([
            new(SortingLayer.FromIndex(1), "Default"),
            new(SortingLayer.FromIndex(2), "Ignore Raycast"),
            new(SortingLayer.FromIndex(8), " Gameplay "),
            new(SortingLayer.FromIndex(9), "Enemies"),
            new(SortingLayer.Ui, "UI")
        ]);
        return configuration;
    }

    public void Dispose()
    {
        TagManager.Configure(_originalTags);
        SortingLayerRegistry.Configure(_originalLayers);
    }
}
