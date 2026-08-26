using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.UIElements;

internal sealed class UIElementsRenderContributor2D(IGraphicsDevice device) : ISceneRenderContributor2D
{
    private readonly UIElementsRenderer _renderer = new(device);

    public string packageId => "com.bengine.ui-elements";
    public bool rendersUi => true;

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder)
        => Collect(scene, camera, width, height, submissions, submissionOrder, null);

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder,
        Predicate<GameObject>? objectFilter)
    {
        var hierarchy = HierarchyOrder2D.Build(scene);
        foreach (var document in scene.QueryComponents<UIDocument>().ToArray()
                     .Where(item => item.enabled && item.gameObject.activeInHierarchy)
                     .Where(item => objectFilter is null || objectFilter(item.gameObject))
                     .OrderBy(item => hierarchy.GetValueOrDefault(item.gameObject)))
        {
            var list = UIRenderListBuilder.Build(document.rootVisualElement, width, height,
                document.ResolveScale(width, height));
            var documentHierarchy = hierarchy.GetValueOrDefault(document.gameObject) * 1_000_000L;
            for (var index = 0; index < list.Commands.Count; index++)
            {
                var command = list.Commands[index];
                var material = ResolveMaterial(command.Element) ?? document.material;
                var atlas = ResolveAtlas(command, document);
                var layer = command.Element.sortingLayer == SortingLayer.Ui
                    ? document.sortingLayer
                    : command.Element.sortingLayer;
                submissions.Add(new RenderSubmission2D(
                    new RenderSortKey2D(layer,
                        checked(document.sortingOrder + command.Element.orderInLayer),
                        documentHierarchy + index,
                        command.Color.A < byte.MaxValue
                            ? RenderTransparency.Transparent
                            : RenderTransparency.Opaque,
                        submissionOrder++),
                    new RenderBatchKey2D(material, atlas),
                    new UICommandPayload(command)));
            }
        }
        return submissionOrder;
    }

    public bool CanRender(RenderBatch2D batch) =>
        batch.Submissions.Count > 0 && batch.Submissions.All(item => item.Payload is UICommandPayload);

    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport) =>
        _renderer.RenderCommands(
            batch.Submissions.Select(item => ((UICommandPayload)item.Payload).Command),
            width, height, viewport);

    public void Dispose() => _renderer.Dispose();

    private static Material? ResolveMaterial(VisualElement element)
    {
        for (var current = element; current is not null; current = current.parent)
            if (current.material is { } material) return material;
        return null;
    }

    private static string ResolveAtlas(UIRenderCommand command, UIDocument document)
    {
        for (var current = command.Element; current is not null; current = current.parent)
            if (!string.IsNullOrWhiteSpace(current.atlas)) return current.atlas;
        if (!string.IsNullOrWhiteSpace(document.atlas)) return document.atlas;
        return command.Type is UIRenderCommandType.Image or UIRenderCommandType.Text
            ? command.Content
            : string.Empty;
    }

    private readonly record struct UICommandPayload(UIRenderCommand Command);
}
