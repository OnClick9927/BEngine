using BEngine.Rendering.Rhi;
using System.Runtime.InteropServices;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed partial class PortableSceneRenderer
{
    private readonly Dictionary<MaskKey, int> _maskLookup = [];
    private readonly List<MaskGroup> _maskGroups = [];
    private int _maskGroupCount;
    private readonly List<float> _maskVertices = [];
    private IGraphicsRenderTarget? _maskCoverage, _maskContent;
    private IGraphicsProgram? _maskProgram;
    private IGraphicsMesh? _maskMesh;
    private IGraphicsTexture2D? _maskWhite;
    private MemoryBudget.Lease? _maskReservation;
    private int _paintedMaskGroup;
    private bool _maskBudgetRejected;
    private readonly record struct MaskKey(Scene? Scene, ulong Layer, int Order, SpriteMaskInteraction Mode);
    private sealed class MaskGroup
    {
        internal readonly List<SpriteMask> Masks = [];
        internal bool Inside;
    }
    private void ResetMaskGroups()
    {
        for (var index = 0; index < _maskGroupCount; index++) _maskGroups[index].Masks.Clear();
        _maskGroupCount = 0;
    }

    private int GetMaskGroup(SpriteRenderer renderer, ReadOnlySpan<SpriteMask> masks)
    {
        if (renderer.maskInteraction == SpriteMaskInteraction.None) return 0;
        var key = new MaskKey(renderer.gameObject.scene, renderer.sortingLayer, renderer.orderInLayer, renderer.maskInteraction);
        if (_maskLookup.TryGetValue(key, out var group)) return group;
        if (_maskGroupCount == _maskGroups.Count) _maskGroups.Add(new());
        var candidate = _maskGroups[_maskGroupCount];
        var applicable = candidate.Masks;
        foreach (var mask in masks)
            if (mask.enabled && mask.gameObject.activeInHierarchy && mask.AppliesTo(renderer)) applicable.Add(mask);
        var inside = renderer.maskInteraction == SpriteMaskInteraction.VisibleInsideMask;
        if (applicable.Count == 0) group = inside ? -1 : 0;
        else { candidate.Inside = inside; group = ++_maskGroupCount; }
        _maskLookup.Add(key, group);
        return group;
    }

    private void DrawMaskedBatch(int index, RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport)
    {
        if (!EnsureMaskResources(viewport.Width, viewport.Height)) return;
        var groupNumber = batch.BatchKey.MaskGroup;
        var group = _maskGroups[groupNumber - 1];
        var offscreen = new GraphicsRect(0, 0, viewport.Width, viewport.Height);
        if (_paintedMaskGroup != groupNumber)
        {
            using (_device.PushRenderTarget(_maskCoverage!))
            {
                PrepareViewport(offscreen);
                _device.SetBlendMode(GraphicsBlendMode.Disabled);
                _device.SetRasterizerState(GraphicsRasterizerState.Default);
                _device.Clear(GraphicsClearFlags.Color, NVector4.Zero);
                foreach (var mask in group.Masks) PaintMask(mask, camera, width, height);
            }
            _paintedMaskGroup = groupNumber;
        }
        using (_device.PushRenderTarget(_maskContent!))
        {
            PrepareViewport(offscreen);
            _device.Clear(GraphicsClearFlags.Color, NVector4.Zero);
            if (_batchContributors[index] is { } contributor)
                using (SceneRenderDiagnostics.MeasureRenderer(contributor))
                    contributor.Render(batch, camera, width, height, offscreen);
            else DrawPreparedBatch(_coreBatchRanges[index], width, height);
        }
        PrepareViewport(viewport);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        _maskVertices.Clear();
        // Counter-clockwise after the viewport's top-left to clip-space conversion.
        Vertex(0, 0, 0, 0); Vertex(0, height, 0, 1); Vertex(width, height, 1, 1);
        Vertex(0, 0, 0, 0); Vertex(width, height, 1, 1); Vertex(width, 0, 1, 0);
        DrawMaskMesh(width, height, new(group.Inside ? 1 : 2, 0, _device.Backend is GraphicsBackend.OpenGL or GraphicsBackend.OpenGLES or GraphicsBackend.WebGL ? 1 : 0, 0),
            _maskContent!.ColorTexture!, _maskCoverage!.ColorTexture!);
        PrepareViewport(viewport);
    }

    private void PaintMask(SpriteMask mask, RenderCamera camera, int width, int height)
    {
        var visual = SpriteSourceRegistry.Resolve(mask.sprite);
        var center = new Vector2((Fix64.Half - mask.pivot.x) * mask.size.x, (Fix64.Half - mask.pivot.y) * mask.size.y);
        var quad = SceneQuad2D.Create(center, 0, mask.size, Color.white, visual.Texture, visual.Uv, false, false, true)
            .WithTransform(mask.transform.localToWorldMatrix);
        _maskVertices.Clear();
        AddTexturedQuad(_maskVertices, quad, camera, width, height);
        DrawMaskMesh(width, height, new(0, (float)Mathf.Clamp01(mask.alphaCutoff), 0, 0),
            mask.sprite is null || !visual.IsTextured ? _maskWhite! : _textures.Resolve(visual.Texture), _maskWhite!);
    }

    private void Vertex(float x, float y, float u, float v)
    {
        _maskVertices.Add(x); _maskVertices.Add(y);
        _maskVertices.Add(1); _maskVertices.Add(1); _maskVertices.Add(1); _maskVertices.Add(1);
        _maskVertices.Add(u); _maskVertices.Add(v);
    }
    private void DrawMaskMesh(int width, int height, NVector4 settings, IGraphicsTexture2D texture, IGraphicsTexture2D mask)
    {
        _maskMesh!.Update(CollectionsMarshal.AsSpan(_maskVertices));
        _maskProgram!.Bind();
        _maskProgram.SetFloat("uViewportWidth", width); _maskProgram.SetFloat("uViewportHeight", height);
        _maskProgram.SetVector4("uMaskSettings", settings);
        _maskProgram.SetInt("uTexture", 0); _maskProgram.SetInt("uMaskTexture", 1);
        _device.BindTexture(0, texture); _device.BindTexture(1, mask);
        _device.Draw(_maskMesh);
    }
    private bool EnsureMaskResources(int width, int height)
    {
        _device.Capabilities.Require(GraphicsDeviceFeatures.RenderTargets | GraphicsDeviceFeatures.SampledTextures);
        if (_maskProgram is null)
        {
            var shaders = GraphicsUtility.Current.ResolveShaderVariant(_device.Capabilities, "Shaders/PortableScene/Mask");
            _maskProgram = _device.CreateProgram(new("BEngine.SpriteMask.Textures", shaders.Vertex, shaders.Fragment)
            {
                UniformBufferSizeBytes = 32,
                Uniforms = [new("uViewportWidth", 0, 1), new("uViewportHeight", 4, 1), new("uMaskSettings", 16, 4)],
                SampledTextureCount = 2
            });
            _maskMesh = _device.CreateMesh(new("BEngine.SpriteMask.Mesh", ReadOnlyMemory<float>.Empty, TexturedVertexLayout,
                GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
            _maskWhite = _device.CreateTexture2D("BEngine.SpriteMask.White", new(1, 1, GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Nearest, GraphicsTextureFilter.Nearest), [255, 255, 255, 255]);
        }
        if (_maskCoverage?.Width == width && _maskCoverage.Height == height &&
            (_maskReservation?.Bytes ?? 0) <= GraphicsTextureMemory.Budget.LimitBytes) return true;
        RetireMaskTargets();
        if (!GraphicsTextureMemory.TryReserve(checked((long)width * height * 8), out _maskReservation))
        {
            if (!_maskBudgetRejected) Debug.LogWarning("The texture budget cannot admit SpriteMask targets; masked batches are skipped until memory is available.");
            _maskBudgetRejected = true; return false;
        }
        try
        {
            var description = new GraphicsRenderTargetDescription(width, height, GraphicsTextureFormat.Rgba8Unorm, null,
                SampleColor: true, Filter: GraphicsTextureFilter.Nearest);
            _maskCoverage = _device.CreateRenderTarget("BEngine.SpriteMask.Coverage", description);
            _maskContent = _device.CreateRenderTarget("BEngine.SpriteMask.Content", description);
        }
        catch { RetireMaskTargets(); throw; }
        _paintedMaskGroup = 0;
        _maskBudgetRejected = false;
        return true;
    }
    private void RetireMaskTargets()
    {
        var targets = new MaskTargets(_maskCoverage, _maskContent, _maskReservation);
        _maskCoverage = _maskContent = null; _maskReservation = null;
        if (_device is IGraphicsResourceDisposal disposal && targets.Reservation is { } reservation)
        {
            targets.Reservation = null; disposal.DisposeResource(targets, reservation); return;
        }
        if (_device is IGraphicsResourceRetirement retirement) retirement.RetireResource(targets);
        else targets.Dispose();
    }
    private void DisposeMasks()
    {
        RetireMaskTargets(); _maskMesh?.Dispose(); _maskProgram?.Dispose(); _maskWhite?.Dispose();
        _maskMesh = null; _maskProgram = null; _maskWhite = null;
    }
    private sealed class MaskTargets(IGraphicsRenderTarget? a, IGraphicsRenderTarget? b, MemoryBudget.Lease? reservation) : IDisposable
    {
        internal MemoryBudget.Lease? Reservation = reservation;
        public void Dispose()
        {
            try { a?.Dispose(); }
            finally { try { b?.Dispose(); } finally { Reservation?.Dispose(); Reservation = null; } }
        }
    }
}
