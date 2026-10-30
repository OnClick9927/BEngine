using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

internal sealed class UIElementsRenderContributor2D(IGraphicsDevice device) : ISceneRenderContributor2D
{
    private readonly UIElementsRenderer _renderer = new(device);
    private readonly List<UIDocument> _documents = [];
    private readonly List<UICommandPayload> _payloads = [];
    private readonly List<UIRenderCommand> _commands = [];
    private readonly ConditionalWeakTable<UIDocument, SubmissionMetadata> _submissionMetadata = new();
    private int _payloadCount;
    private bool _hadDocuments;
    private bool _frameHasDocuments;

    public string packageId => "com.bengine.ui-elements";
    public bool rendersUi => true;
    public bool processesQuads => false;

    public void BeginFrame() { _frameHasDocuments = false; _payloadCount = 0; }
    public void EndFrame()
    {
        // Submissions borrow these facades only until the frame ends. Release
        // element references so an unloaded scene is not retained by the pool.
        for (var index = 0; index < _payloadCount; index++) _payloads[index].Command = default;
        _commands.Clear(); _documents.Clear();
        if (!_frameHasDocuments && _hadDocuments) _renderer.ClearTextureCache();
        _hadDocuments = _frameHasDocuments;
    }

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder)
        => Collect(scene, camera, width, height, submissions, submissionOrder, null);

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder,
        Predicate<GameObject>? objectFilter)
    {
        _documents.Clear();
        _documents.AddRange(UIDocumentInputRouter.Collect(scene));
        if (objectFilter is null)
            _documents.RemoveAll(static item => !item.isVisible);
        else
            for (var index = _documents.Count - 1; index >= 0; index--)
                if (!_documents[index].isVisible || !objectFilter(_documents[index].gameObject))
                    _documents.RemoveAt(index);
        _frameHasDocuments |= _documents.Count > 0;
        // Only this exact built-in collection has a known, callback-free Add.
        // Derived or user collections retain the per-command invalidation guard.
        var directSubmissions = submissions?.GetType() == typeof(List<RenderSubmission2D>)
            ? (List<RenderSubmission2D>)submissions : null;
        foreach (var document in _documents)
        {
            var root = document.rootVisualElement;
            if (directSubmissions is not null)
            {
                using var commands = BuildDiagnosedRendererCommands(document, root, width, height);
                // Opt-in is local to this document. Build first: layout/style or
                // collection callbacks may change its live sorting layer. Only
                // the exact List path has callback-free submission Add calls.
                if (document.cullExcludedCameraSubmissions && !camera.ContainsLayer(document.sortingLayer))
                {
                    // Match the old per-command ++ wrap semantics so subsequent
                    // contributors retain their original submission-order keys.
                    submissionOrder = unchecked(submissionOrder + commands.Commands.Count);
                    continue;
                }
                submissionOrder = Submit(document, root, commands.Commands, commands.Identity, commands.Revision,
                    submissions!, directSubmissions, submissionOrder);
            }
            else
            {
                // An arbitrary Add can call Build or mutate the document, so
                // these callers continue to receive a stable public snapshot.
                var list = BuildDiagnosedPublicCommands(document, root, width, height);
                submissionOrder = Submit(document, root, list.Commands, list, 0, submissions!, directSubmissions, submissionOrder);
            }
        }
        return submissionOrder;
    }

    private static UIRenderListBuilder.RendererCommandLease BuildDiagnosedRendererCommands(
        UIDocument document, VisualElement root, int width, int height)
    {
        using var diagnostic = SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiLayoutAndCommands);
        return UIRenderListBuilder.BuildForRenderer(root, width, height, document.ResolveScale(width, height));
    }

    private static UIRenderCommandList BuildDiagnosedPublicCommands(
        UIDocument document, VisualElement root, int width, int height)
    {
        using var diagnostic = SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiLayoutAndCommands);
        return UIRenderListBuilder.Build(root, width, height, document.ResolveScale(width, height));
    }

    private long Submit(UIDocument document, VisualElement root, IReadOnlyList<UIRenderCommand> commands,
        object identity, long revision, ICollection<RenderSubmission2D> submissions,
        List<RenderSubmission2D>? directSubmissions, long submissionOrder)
    {
        using var diagnostic = SceneRenderDiagnostics.Measure(SceneRenderDiagnosticPhase.UiSubmit);
        var documentSubmissionOrder = submissionOrder;
        var metadata = _submissionMetadata.GetValue(document, static item => new SubmissionMetadata(item));
        var prepared = metadata.Prepare(document, commands, identity, revision, root);
        // The exact built-in List cannot call user code from Add. Its stable
        // ordered stream can reuse value templates; payloads still belong to
        // this camera/frame and are copied separately below.
        var templates = directSubmissions is not null && prepared && metadata.ReusedEntries;
        var reuseSort = templates && metadata.BeginSortTemplates(document, documentSubmissionOrder, commands.Count);
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            // Add may belong to a user collection that mutates the UI. Do
            // not reuse the prepared ancestry after any such mutation.
            var current = prepared && (directSubmissions is not null || metadata.IsCurrent(document));
            var material = current ? metadata.Entries[index].Material : ResolveMaterial(command.Element) ?? document.material;
            var atlas = current ? metadata.Entries[index].Atlas : ResolveAtlas(command, document);
            var layer = document.sortingLayer;
            if (_payloadCount == _payloads.Count) _payloads.Add(new UICommandPayload());
            var payload = _payloads[_payloadCount++];
            // Render and later cameras own this value, not the leased list.
            payload.Command = command;
            var sort = reuseSort ? metadata.Templates[index].Sort : new RenderSortKey2D(layer, 0, index,
                    command.Color.A < byte.MaxValue ? RenderTransparency.Transparent : RenderTransparency.Opaque,
                    submissionOrder, 0, document.sortingOrder, documentSubmissionOrder);
            submissionOrder++;
            if (templates && !reuseSort) metadata.Templates[index].Sort = sort;
            var submission = new RenderSubmission2D(sort,
                // Material shader/name are live asset state rather than UI versions.
                templates ? metadata.ResolveBatch(index) :
                    new RenderBatchKey2D(material, atlas) { PayloadType = typeof(UICommandPayload) }, payload);
            if (directSubmissions is not null) directSubmissions.Add(submission);
            else submissions.Add(submission);
        }
        if (templates) metadata.CompleteSortTemplates(document, documentSubmissionOrder);
        return submissionOrder;
    }

    public bool CanRender(RenderBatch2D batch) =>
        batch.HasOnlyPayloads<UICommandPayload>();

    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport)
    {
        _commands.Clear();
        foreach (ref readonly var submission in batch.SubmissionSpan)
            _commands.Add(((UICommandPayload)submission.Payload).Command);
        _renderer.RenderCommands(_commands, width, height, viewport);
    }

    public void Dispose()
    {
        foreach (var entry in _submissionMetadata) entry.Value.Detach(entry.Key);
        _commands.Clear(); _payloads.Clear(); _documents.Clear(); _submissionMetadata.Clear();
        _renderer.Dispose();
    }

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
        if (document.atlas is { } atlas) return atlas.assetPath;
        // The UI renderer resolves and batches textures within this ordered stream.
        // Splitting the outer scene batch by every caption/image repeatedly builds
        // and uploads the same dynamic meshes, preventing safe UI batching.
        return string.Empty;
    }

    private sealed class UICommandPayload
    {
        internal UIRenderCommand Command;
    }

    private sealed class SubmissionMetadata
    {
        internal SubmissionMetadata(UIDocument document) => document.visualTreeChanged += OnVisualTreeChanged;

        internal Entry[] Entries = [];
        internal Template[] Templates = [];
        internal bool ReusedEntries { get; private set; }
        private long _entryRevision;
        private object? _list;
        private long _listRevision;
        private VisualElement? _root;
        private long _rootVersion, _assetGeneration;
        private Material? _material;
        private BAsset? _atlas;
        private string? _atlasPath;
        private bool _sortTemplatesReady;
        private ulong _sortingLayer;
        private long _submissionOrder;
        private int _sortingOrder;

        internal bool BeginSortTemplates(UIDocument document, long order, int count)
        {
            if (Templates.Length < count) { Templates = new Template[count]; _sortTemplatesReady = false; }
            if (_sortTemplatesReady && _sortingLayer == document.sortingLayer &&
                _sortingOrder == document.sortingOrder && _submissionOrder == order) return true;
            // A later live material getter may throw. Partially rebuilt keys
            // must never be mistaken for the preceding complete template.
            _sortTemplatesReady = false;
            return false;
        }

        internal void CompleteSortTemplates(UIDocument document, long order)
        {
            _sortingLayer = document.sortingLayer; _sortingOrder = document.sortingOrder;
            _submissionOrder = order; _sortTemplatesReady = true;
        }

        internal bool IsCurrent(UIDocument document) =>
            _root is not null && _root.renderVersion == _rootVersion &&
            BAsset.cacheGeneration == _assetGeneration && ReferenceEquals(document.material, _material) &&
            ReferenceEquals(document.atlas, _atlas) && document.atlas?.assetPath == _atlasPath;

        internal bool Prepare(UIDocument document, IReadOnlyList<UIRenderCommand> commands,
            object identity, long revision, VisualElement root)
        {
            // Applying styles while building the list may replace the document's
            // tree. Submit that original list as before, without retaining it or
            // associating its commands with the replacement root's version.
            if (!ReferenceEquals(document.rootVisualElement, root)) { Clear(); return false; }
            ReusedEntries = false;
            if (ReferenceEquals(_list, identity) && _listRevision == revision && IsCurrent(document))
            { ReusedEntries = true; return true; }
            var version = root.renderVersion;
            var generation = BAsset.cacheGeneration;
            var material = document.material;
            var atlas = document.atlas;
            var atlasPath = atlas?.assetPath;
            _sortTemplatesReady = false;
            _entryRevision++;
            if (Entries.Length < commands.Count)
                Entries = new Entry[commands.Count];
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                Entries[index] = new Entry(ResolveMaterial(command.Element) ?? material, ResolveAtlas(command, document));
            }
            // A smaller replacement must release materials held by old entries.
            Array.Clear(Entries, commands.Count, Entries.Length - commands.Count);
            if (Templates.Length > commands.Count)
                Array.Clear(Templates, commands.Count, Templates.Length - commands.Count);
            _list = identity; _listRevision = revision; _root = root; _rootVersion = version; _assetGeneration = generation;
            _material = material; _atlas = atlas; _atlasPath = atlasPath;
            return true;
        }

        private void OnVisualTreeChanged(UIDocument document) => Clear();

        internal void Detach(UIDocument document)
        {
            document.visualTreeChanged -= OnVisualTreeChanged;
            Clear();
        }

        private void Clear()
        {
            Entries = [];
            Templates = []; ReusedEntries = false;
            _sortTemplatesReady = false;
            _list = null; _root = null; _material = null; _atlas = null; _atlasPath = null;
        }

        internal readonly record struct Entry(Material Material, string Atlas);
        internal struct Template
        {
            internal RenderSortKey2D Sort;
            internal long Revision;
            internal RenderBatchKey2D Batch;
        }
        internal RenderBatchKey2D ResolveBatch(int index)
        {
            ref var template = ref Templates[index]; var entry = Entries[index];
            // Shader changes need not raise a UI dirty event. Preserve the
            // original per-command getter/exception timing and rebuild only
            // this immutable key when its live shader name changes.
            var shader = entry.Material.shader.shaderName;
            if (template.Revision != _entryRevision || !string.Equals(template.Batch.Shader, shader, StringComparison.Ordinal))
            {
                template.Batch = new RenderBatchKey2D(entry.Material, entry.Atlas) { PayloadType = typeof(UICommandPayload) };
                template.Revision = _entryRevision;
            }
            return template.Batch;
        }
    }
}
