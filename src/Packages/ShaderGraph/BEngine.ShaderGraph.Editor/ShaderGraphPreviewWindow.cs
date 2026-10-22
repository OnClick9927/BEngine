using BEngine.Editor;
using BEngine.Rendering.Rhi;

namespace BEngine.ShaderGraph.Editor;

/// <summary>An editor graphics viewport; it uses the host's actual GPU and does not create a player scene.</summary>
public sealed class ShaderGraphPreviewWindow : EditorWindow
{
    private readonly PreviewContent _preview;
    public ShaderGraphPreviewWindow()
    {
        titleContent = new("Shader Node Preview"); minSize = new(300, 280);
        content = _preview = new PreviewContent(this);
    }

    public static ShaderGraphPreviewWindow Open(ShaderGraphAsset graph, string? nodeId = null)
    {
        var window = GetWindow<ShaderGraphPreviewWindow>("Shader Node Preview");
        window.SetTarget(graph, nodeId); window.ShowUtility(); return window;
    }
    public void SetTarget(ShaderGraphAsset graph, string? nodeId = null)
    { _preview.Graph = graph; _preview.NodeId = nodeId; Repaint(); }
    public void SetMaterial(ShaderGraphMaterial? material) { _preview.Material = material; Repaint(); }
    protected override void OnInspectorUpdate() => Repaint();
    protected override void OnGUI()
    {
        GUILayout.Label(_preview.NodeId is null ? "Graph output" : "Node: " + _preview.NodeId, EditorStyles.boldLabel);
        _preview.Material = EditorGUILayout.ObjectField("Material", _preview.Material, false);
        _preview.Texture = EditorGUILayout.ObjectField("Sprite texture", _preview.Texture, false);
        _preview.Animate = EditorGUILayout.Toggle("Animate time", _preview.Animate);
        if (!_preview.Animate) _preview.Time = (Fix64)EditorGUILayout.FloatField("Time", (float)_preview.Time);
        if (_preview.Error is { } error) EditorGUILayout.HelpBox(error, MessageType.Error);
    }

    private sealed class PreviewContent(ShaderGraphPreviewWindow owner) : IEditorWindowContent
    {
        private readonly Dictionary<IGraphicsDevice, ShaderGraphPreviewRenderer> _renderers = new(ReferenceEqualityComparer.Instance);
        private Rect _rect;
        public ShaderGraphAsset? Graph;
        public string? NodeId, Error;
        public ShaderGraphMaterial? Material;
        public Texture? Texture;
        public bool Animate = true;
        public Fix64 Time;
        public void SynchronizeLayout(Fix64 width, Fix64 height) => _rect = new(6, 150, Fix64.Max(1, width - 12), Fix64.Max(1, height - 156));
        public bool TryGetGraphicsViewport(out EditorGraphicsViewport viewport)
        {
            viewport = new(_rect, Math.Max(1, (int)_rect.width), Math.Max(1, (int)_rect.height));
            return Graph is { isDestroyed: false } && viewport.IsValid;
        }
        public void RenderGraphicsViewport(IGraphicsDevice device, GraphicsRect viewport, int targetWidth, int targetHeight)
        {
            if (Graph is not { isDestroyed: false } graph) return;
            if (!_renderers.TryGetValue(device, out var renderer)) _renderers.Add(device, renderer = new(device));
            device.SetViewport(viewport);
            device.SetScissor(new(0, 0, viewport.Width, viewport.Height));
            device.SetDepthState(GraphicsDepthState.Disabled);
            device.SetRasterizerState(GraphicsRasterizerState.Default);
            device.SetBlendMode(GraphicsBlendMode.Disabled);
            try
            {
                renderer.Render(graph, NodeId, targetWidth, targetHeight, Animate ? (Fix64)EditorApplication.timeSinceStartup : Time, Material, Texture);
            }
            finally { device.SetScissor(null); device.SetDepthState(GraphicsDepthState.Default); }
            if (Error != renderer.Error) { Error = renderer.Error; owner.Repaint(); }
        }
        public void HandleEvent(Event current) { }
        public void ReleaseGraphicsDevice(IGraphicsDevice device)
        { if (_renderers.Remove(device, out var renderer)) renderer.Dispose(); }
        public void ReleaseGraphicsDevices()
        { foreach (var renderer in _renderers.Values) renderer.Dispose(); _renderers.Clear(); }
    }
}
