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
            new(SortingLayer.Default, "Default", true),
            new(SortingLayer.TransparentFx, "TransparentFX", true),
            new(SortingLayer.IgnoreRaycast, "Ignore Raycast", true),
            new(SortingLayer.Water, "Water", true),
            new(SortingLayer.Ui, "UI", true, true),
            new(SortingLayer.FromIndex(6), " Gameplay "),
            new(SortingLayer.FromIndex(7), "Enemies")
        ]);
        return configuration;
    }

    public void Dispose()
    {
        TagManager.Configure(_originalTags);
        SortingLayerRegistry.Configure(_originalLayers);
    }
}
