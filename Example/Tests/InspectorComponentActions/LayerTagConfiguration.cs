namespace BEngine.ExampleTests.InspectorComponentActions;

internal sealed class LayerTagConfiguration : IDisposable
{
    private readonly string[] _tags = TagManager.tags.ToArray();
    private readonly SortingLayerDefinition[] _layers = LayerMask.layers.ToArray();

    internal LayerTagConfiguration()
    {
        TagManager.Configure(["Untagged", "Player", "Enemy"]);
        SortingLayerRegistry.Configure([
            new(SortingLayer.Default, "Default", true),
            new(SortingLayer.TransparentFx, "TransparentFX", true),
            new(SortingLayer.FromIndex(3), "Gameplay", true),
            new(SortingLayer.FromIndex(4), "Enemies", true),
            new(SortingLayer.Ui, "UI", true, true)
        ]);
    }

    public void Dispose()
    {
        TagManager.Configure(_tags);
        SortingLayerRegistry.Configure(_layers);
    }
}
