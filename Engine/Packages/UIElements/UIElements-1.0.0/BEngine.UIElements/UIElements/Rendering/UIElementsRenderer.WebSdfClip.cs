using BEngine.UIElements;
using BEngine.Rendering.Rhi;
using System.Runtime.InteropServices;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer
{
    private const string WebClipBuiltinVertex = "#version 330 core\nlayout (location = 0) in vec2 aPosition;\nlayout (location = 1) in vec4 aColor;\nlayout (location = 2) in vec2 aTexCoord;\nuniform float uViewportWidth;\nuniform float uViewportHeight;\nout vec4 vColor;\nout vec2 vTexCoord;\nvoid main()\n{\n    vec2 normalized = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);\n    gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);\n    vColor = aColor;\n    vTexCoord = aTexCoord;\n}\n";
    private const string WebClipBuiltinFragment = "#version 330 core\nin vec4 vColor;\nin vec2 vTexCoord;\nuniform sampler2D uTexture;\nuniform float uSdfThreshold;\nout vec4 fragColor;\nvoid main() {\n    float distance = texture(uTexture, vTexCoord).a/*BEngineSdfDistance*/;\n    float smoothing = max(fwidth(distance) * 0.5, 0.003922);\n    float coverage = smoothstep(uSdfThreshold - smoothing, uSdfThreshold + smoothing, distance);\n    fragColor = vec4(vColor.rgb, vColor.a * coverage);\n}\n";

    // Candidate only. Old UI batches, planner bounds and SDF cache stay intact.
    internal bool EnableWebSdfClipBatching { get; set; } = true;
    private const int WebClipStride = 12;
    private const int MaximumWebClipFloats = 4 * 1024 * 1024;
    private const int MaximumExactClipCoordinate = 16777215;
    private static readonly GraphicsVertexLayout WebClipLayout = new(WebClipStride * sizeof(float),
        [new(0, 2, 0), new(1, 4, 2 * sizeof(float)), new(2, 2, 6 * sizeof(float)), new(3, 4, 8 * sizeof(float))]);
    private GraphicsShaderPair? _webClipSources;
    private IGraphicsProgram? _webClipShader;
    private IGraphicsMesh? _webClipMesh;
    private GraphicsMeshUploadCache? _webClipUploads;
    private readonly List<float> _webClipVertices = [];
    private readonly List<WebClipRange> _webClipRanges = [];
    private readonly record struct WebClipRange(int FirstVertex, int VertexCount, bool Valid);

    private void CaptureWebClipSources(GraphicsShaderPair original, GraphicsShaderPair configured)
    {
        if (_device.Backend != GraphicsBackend.WebGL ||
            original.Vertex.Stage != GraphicsShaderStage.Vertex || original.Fragment.Stage != GraphicsShaderStage.Fragment ||
            original.Vertex.Language != GraphicsShaderLanguage.Glsl || original.Fragment.Language != GraphicsShaderLanguage.Glsl ||
            original.Vertex.EntryPoint != "main" || original.Fragment.EntryPoint != "main" ||
            original.Vertex.Code.ReplaceLineEndings("\n").Trim() != WebClipBuiltinVertex.Trim() ||
            original.Fragment.Code.ReplaceLineEndings("\n").Trim() != WebClipBuiltinFragment.Trim()) return;
        var vertex = configured.Vertex.Code.Replace("out vec4 vColor;",
            "layout (location = 3) in vec4 aClipRect;\nflat out vec4 vClipRect;\nout vec4 vColor;", StringComparison.Ordinal)
            .Replace("    vColor = aColor;", "    vClipRect = aClipRect;\n    vColor = aColor;", StringComparison.Ordinal);
        // Derivatives and coverage retain their original control flow. The
        // integer scissor mask is applied afterwards, before the color write.
        var fragment = configured.Fragment.Code.Replace("in vec4 vColor;", "flat in vec4 vClipRect;\nin vec4 vColor;", StringComparison.Ordinal)
            .Replace("    fragColor =", "    if (gl_FragCoord.x < vClipRect.x || gl_FragCoord.y < vClipRect.y ||\n        gl_FragCoord.x >= vClipRect.z || gl_FragCoord.y >= vClipRect.w) discard;\n    fragColor =", StringComparison.Ordinal);
        _webClipSources = new(configured.Vertex with { Code = vertex }, configured.Fragment with { Code = fragment });
    }

    private void PrepareWebClipGeometry(List<RenderBatch> batches, int width, int height, GraphicsRect viewport)
    {
        _webClipVertices.Clear(); _webClipRanges.Clear();
        if (!EnableWebSdfClipBatching || _webClipSources is null) return;
        foreach (var batch in CollectionsMarshal.AsSpan(batches))
        {
            var valid = batch.Texture is not null && batch.SdfThreshold.HasValue &&
                batch.Vertices.Count % 48 == 0 && batch.Vertices.Count / 8 <=
                (MaximumWebClipFloats - _webClipVertices.Count) / WebClipStride;
            GraphicsRect scissor = default;
            long left = 0, bottom = 0, right = 0, top = 0;
            if (valid)
            {
                try
                {
                    scissor = ToGraphicsRect(batch.ClipRect, width, height, viewport.Width, viewport.Height);
                    left = (long)viewport.X + scissor.X;
                    bottom = (long)viewport.Y + viewport.Height - scissor.Y - scissor.Height;
                    right = left + scissor.Width; top = bottom + scissor.Height;
                    valid = left >= -MaximumExactClipCoordinate && bottom >= -MaximumExactClipCoordinate &&
                        right <= MaximumExactClipCoordinate && top <= MaximumExactClipCoordinate &&
                        right >= left && top >= bottom;
                }
                catch (OverflowException) { valid = false; }
                catch (ArgumentOutOfRangeException) { valid = false; }
            }
            var sourceVertices = CollectionsMarshal.AsSpan(batch.Vertices);
            if (valid)
                foreach (var value in sourceVertices)
                    if (!float.IsFinite(value)) { valid = false; break; }
            if (!valid) { _webClipRanges.Add(default); continue; }
            var first = _webClipVertices.Count / WebClipStride;
            var required = checked(_webClipVertices.Count + batch.Vertices.Count / 8 * WebClipStride);
            if (_webClipVertices.Capacity < required)
                _webClipVertices.Capacity = Math.Min(MaximumWebClipFloats, Math.Max(required, _webClipVertices.Capacity * 2));
            CollectionsMarshal.SetCount(_webClipVertices, required);
            var destination = CollectionsMarshal.AsSpan(_webClipVertices).Slice(first * WebClipStride);
            float clipLeft = left, clipBottom = bottom, clipRight = right, clipTop = top;
            for (int sourceIndex = 0, targetIndex = 0; sourceIndex < sourceVertices.Length;
                sourceIndex += 8, targetIndex += WebClipStride)
            {
                sourceVertices.Slice(sourceIndex, 8).CopyTo(destination.Slice(targetIndex, 8));
                destination[targetIndex + 8] = clipLeft;
                destination[targetIndex + 9] = clipBottom;
                destination[targetIndex + 10] = clipRight;
                destination[targetIndex + 11] = clipTop;
            }
            _webClipRanges.Add(new(first, batch.Vertices.Count / 8, true));
        }
    }

    private void UploadWebClipGeometry()
    {
        if (_webClipVertices.Count == 0) return;
        if (_webClipMesh is null)
        {
            _webClipMesh = _device.CreateMesh(new("BEngine.UIElements.WebSdfClipMesh", ReadOnlyMemory<float>.Empty,
                WebClipLayout, usage: GraphicsBufferUsage.Dynamic));
            _webClipUploads = new(_webClipMesh);
        }
        _webClipUploads!.Update(CollectionsMarshal.AsSpan(_webClipVertices));
    }

    private bool TryDrawWebClipRun(List<RenderBatch> batches, ref int index, int width, int height,
        ref IGraphicsProgram? activeProgram, out int vertexCount)
    {
        vertexCount = 0;
        if (index >= _webClipRanges.Count || !_webClipRanges[index].Valid) return false;
        var first = _webClipRanges[index].FirstVertex;
        var batch = batches[index];
        vertexCount = _webClipRanges[index].VertexCount;
        while (index + 1 < batches.Count && _webClipRanges[index + 1].Valid)
        {
            var next = batches[index + 1];
            if (!ReferenceEquals(next.Texture, batch.Texture) || next.SdfThreshold != batch.SdfThreshold || next.Opaque != batch.Opaque) break;
            index++; vertexCount += _webClipRanges[index].VertexCount;
        }
        if (_webClipShader is null)
        {
            var sources = _webClipSources!.Value;
            _webClipShader = _device.CreateProgram(new("BEngine.UIElements.WebSdfClip", sources.Vertex, sources.Fragment)
                { Uniforms = [new("uViewportWidth", 0, 1), new("uViewportHeight", 4, 1), new("uSdfThreshold", 8, 1)] });
        }
        _device.SetBlendMode(batch.Opaque ? GraphicsBlendMode.Disabled : GraphicsBlendMode.AlphaBlend);
        _device.SetScissor(null);
        if (!ReferenceEquals(activeProgram, _webClipShader))
        {
            activeProgram = _webClipShader; BindProgram(activeProgram, width, height); activeProgram.SetInt("uTexture", 0);
        }
        activeProgram.SetFloat("uSdfThreshold", batch.SdfThreshold!.Value);
        _device.BindTexture(0, batch.Texture!);
        _device.Draw(_webClipMesh!, vertexCount, first);
        return true;
    }

    private void DisposeWebClip()
    {
        _webClipUploads?.Dispose(); _webClipMesh?.Dispose(); _webClipShader?.Dispose();
        _webClipVertices.Clear(); _webClipRanges.Clear(); _webClipSources = null;
    }
}
