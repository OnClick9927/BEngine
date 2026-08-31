namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class RenderOrderingTests
{
    public static void Run()
    {
        var sharedMaterial = Guid.NewGuid();
        var otherMaterial = Guid.NewGuid();
        var sharedBatch = new RenderBatchKey2D(sharedMaterial, "Sprite", "atlas-a");
        var splitByAtlas = new RenderBatchKey2D(sharedMaterial, "Sprite", "atlas-b");
        var splitByShader = new RenderBatchKey2D(sharedMaterial, "Particle", "atlas-a");
        var splitByMaterial = new RenderBatchKey2D(otherMaterial, "Sprite", "atlas-a");

        var submissions = new[]
        {
            Submit("ui", SortingLayer.Ui, 0, 0, RenderTransparency.Opaque, 6, sharedBatch),
            Submit("transparent", SortingLayer.Default, 0, 0, RenderTransparency.Transparent, 5, sharedBatch),
            Submit("hierarchy-1", SortingLayer.Default, 0, 1, RenderTransparency.Opaque, 4, sharedBatch),
            Submit("order-1", SortingLayer.Default, 1, 0, RenderTransparency.Opaque, 3, splitByAtlas),
            Submit("shader", SortingLayer.Default, 2, 0, RenderTransparency.Opaque, 2, splitByShader),
            Submit("material", SortingLayer.Default, 3, 0, RenderTransparency.Opaque, 1, splitByMaterial),
            Submit("world", SortingLayer.Default, 0, 0, RenderTransparency.Opaque, 0, sharedBatch)
        };

        var batches = RenderBatchBuilder2D.Build(submissions);
        var ordered = batches.SelectMany(batch => batch.Submissions)
            .Select(item => (string)item.Payload).ToArray();
        TestAssert.Require(ordered.SequenceEqual([
                "world", "transparent", "hierarchy-1", "order-1", "shader", "material", "ui"
            ]),
            "Render sorting did not apply layer/order/hierarchy/transparency/submission precedence.");
        TestAssert.Require(batches.Count == 5 && batches[0].Submissions.Count == 3,
            "Batching did not merge only adjacent Material/Shader/Atlas-compatible submissions.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new RenderSortKey2D(64, 0, 0, RenderTransparency.Opaque, 0),
            "RenderSortKey2D accepted an out-of-range layer index.");

        var scene = new Scene("Renderer layer boundaries");
        var sprite = scene.CreateGameObject("Sprite").AddComponent<SpriteRenderer>();
        TestAssert.Throws<ArgumentOutOfRangeException>(() => sprite.sortingLayer = SortingLayer.Ui,
            "SpriteRenderer accepted a UI sorting layer.");
        var particles = scene.CreateGameObject("UI particles").AddComponent<ParticleSystem2D>();
        particles.sortingLayer = SortingLayer.Ui;
        TestAssert.Require(particles.sortingLayer == SortingLayer.Ui,
            "ParticleSystem2D cannot interleave with UI submissions.");
    }

    private static RenderSubmission2D Submit(string payload, ulong layer, int order,
        long hierarchy, RenderTransparency transparency, long submission,
        RenderBatchKey2D batch) =>
        new(new RenderSortKey2D(layer, order, hierarchy, transparency, submission), batch, payload);
}
