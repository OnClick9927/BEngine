using BEngine.UIElements;
using BEngine.Rendering.Rhi;
using System.Runtime.InteropServices;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer
{
    private const string WebSolidBuiltinVertex = "#version 330 core\nlayout (location = 0) in vec2 aPosition;\nlayout (location = 1) in vec4 aColor;\nuniform float uViewportWidth;\nuniform float uViewportHeight;\nout vec4 vColor;\nvoid main()\n{\n    vec2 normalized = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);\n    gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);\n    vColor = aColor;\n}\n";
    private const string WebSolidBuiltinFragment = "#version 330 core\nin vec4 vColor;\nout vec4 fragColor;\nvoid main() { fragColor = vColor; }\n";
    internal bool EnableWebSolidClipBatching { get; set; } = true;
    private const int WebSolidClipStride = 10;
    private const int MaximumWebSolidClipFloats = 4 * 1024 * 1024;
    private const int MaximumExactSolidClipCoordinate = 16777215;
    private static readonly GraphicsVertexLayout WebSolidClipLayout = new(WebSolidClipStride * sizeof(float),
        [new(0, 2, 0), new(1, 4, 2 * sizeof(float)), new(2, 4, 6 * sizeof(float))]);
    private GraphicsShaderPair? _webSolidClipSources;
    private IGraphicsProgram? _webSolidClipShader;
    private IGraphicsMesh? _webSolidClipMesh;
    private GraphicsMeshUploadCache? _webSolidClipUploads;
    private readonly List<float> _webSolidClipVertices = [];
    private readonly List<WebSolidClipRange> _webSolidClipRanges = [];
    private readonly record struct WebSolidClipRange(int FirstVertex, int VertexCount,
        float Left, float Bottom, float Right, float Top, bool Valid);

    private void CaptureWebSolidSources(GraphicsShaderPair original)
    {
        if (_device.Backend != GraphicsBackend.WebGL ||
            original.Vertex.Stage != GraphicsShaderStage.Vertex || original.Fragment.Stage != GraphicsShaderStage.Fragment ||
            original.Vertex.Language != GraphicsShaderLanguage.Glsl || original.Fragment.Language != GraphicsShaderLanguage.Glsl ||
            original.Vertex.EntryPoint != "main" || original.Fragment.EntryPoint != "main" ||
            original.Vertex.Code.ReplaceLineEndings("\n").Trim() != WebSolidBuiltinVertex.Trim() ||
            original.Fragment.Code.ReplaceLineEndings("\n").Trim() != WebSolidBuiltinFragment.Trim()) return;
        var vertex = original.Vertex.Code.Replace("out vec4 vColor;",
            "layout (location = 2) in vec4 aClipRect;\nflat out vec4 vClipRect;\nout vec4 vColor;", StringComparison.Ordinal)
            .Replace("    vColor = aColor;", "    vClipRect = aClipRect;\n    vColor = aColor;", StringComparison.Ordinal);
        var fragment = original.Fragment.Code.Replace("in vec4 vColor;", "flat in vec4 vClipRect;\nin vec4 vColor;", StringComparison.Ordinal)
            .Replace("fragColor = vColor;", "if (gl_FragCoord.x < vClipRect.x || gl_FragCoord.y < vClipRect.y || " +
                "gl_FragCoord.x >= vClipRect.z || gl_FragCoord.y >= vClipRect.w) discard; fragColor = vColor;", StringComparison.Ordinal);
        _webSolidClipSources = new(original.Vertex with { Code = vertex }, original.Fragment with { Code = fragment });
    }

    private void PrepareWebSolidClipGeometry(List<RenderBatch> batches, int width, int height, GraphicsRect viewport)
    {
        _webSolidClipVertices.Clear(); _webSolidClipRanges.Clear();
        if (!EnableWebSolidClipBatching || _webSolidClipSources is null || batches.Count < 2) return;
        if (_webSolidClipRanges.Capacity < batches.Count) _webSolidClipRanges.Capacity = batches.Count;
        CollectionsMarshal.SetCount(_webSolidClipRanges, batches.Count);
        CollectionsMarshal.AsSpan(_webSolidClipRanges).Clear();
        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            if (batch.Texture is not null || batch.SdfThreshold.HasValue || !batch.Opaque ||
                batch.Vertices.Count == 0 || batch.Vertices.Count % 18 != 0) continue;
            var valid = true;
            foreach (var value in CollectionsMarshal.AsSpan(batch.Vertices))
                if (!float.IsFinite(value)) { valid = false; break; }
            if (!valid) continue;
            try
            {
                var scissor = ToGraphicsRect(batch.ClipRect, width, height, viewport.Width, viewport.Height);
                // Preserve the hardware backend's Int32 intermediate order. A
                // wrapped intermediate keeps its original hardware-scissor path.
                long left = (long)viewport.X + scissor.X;
                long yPlusHeight = (long)viewport.Y + viewport.Height;
                long yMinusTop = yPlusHeight - scissor.Y;
                long bottom = yMinusTop - scissor.Height;
                if (left < int.MinValue || left > int.MaxValue ||
                    yPlusHeight < int.MinValue || yPlusHeight > int.MaxValue ||
                    yMinusTop < int.MinValue || yMinusTop > int.MaxValue ||
                    bottom < int.MinValue || bottom > int.MaxValue) continue;
                long right = left + scissor.Width, top = bottom + scissor.Height;
                if (scissor.Width < 0 || scissor.Height < 0 ||
                    left < -MaximumExactSolidClipCoordinate || left > MaximumExactSolidClipCoordinate ||
                    bottom < -MaximumExactSolidClipCoordinate || bottom > MaximumExactSolidClipCoordinate ||
                    right < -MaximumExactSolidClipCoordinate || right > MaximumExactSolidClipCoordinate ||
                    top < -MaximumExactSolidClipCoordinate || top > MaximumExactSolidClipCoordinate) continue;
                _webSolidClipRanges[i] = new(0, batch.Vertices.Count / 6, left, bottom, right, top, false);
            }
            catch (OverflowException) { }
            catch (ArgumentOutOfRangeException) { }
        }
        for (var start = 0; start < batches.Count;)
        {
            if (_webSolidClipRanges[start].VertexCount == 0) { start++; continue; }
            var end = start + 1;
            long vertices = _webSolidClipRanges[start].VertexCount;
            while (end < batches.Count && _webSolidClipRanges[end].VertexCount != 0)
                vertices += _webSolidClipRanges[end++].VertexCount;
            if (end - start < 2 || vertices > (MaximumWebSolidClipFloats - _webSolidClipVertices.Count) / WebSolidClipStride)
            { start = end; continue; }
            var first = _webSolidClipVertices.Count / WebSolidClipStride;
            var required = _webSolidClipVertices.Count + (int)vertices * WebSolidClipStride;
            if (_webSolidClipVertices.Capacity < required)
                _webSolidClipVertices.Capacity = Math.Min(MaximumWebSolidClipFloats,
                    Math.Max(required, _webSolidClipVertices.Capacity * 2));
            CollectionsMarshal.SetCount(_webSolidClipVertices, required);
            var destination = CollectionsMarshal.AsSpan(_webSolidClipVertices);
            for (var index = start; index < end; index++)
            {
                var range = _webSolidClipRanges[index];
                var source = CollectionsMarshal.AsSpan(batches[index].Vertices);
                var target = first * WebSolidClipStride;
                for (var sourceIndex = 0; sourceIndex < source.Length; sourceIndex += 6, target += WebSolidClipStride)
                {
                    source.Slice(sourceIndex, 6).CopyTo(destination.Slice(target, 6));
                    destination[target + 6] = range.Left;
                    destination[target + 7] = range.Bottom;
                    destination[target + 8] = range.Right;
                    destination[target + 9] = range.Top;
                }
                _webSolidClipRanges[index] = range with { FirstVertex = first, Valid = true };
                first += range.VertexCount;
            }
            start = end;
        }
    }

    private void UploadWebSolidClipGeometry()
    {
        if (_webSolidClipVertices.Count == 0) return;
        // As with WebSdfClip, device/resource exceptions propagate. Do not hide
        // external device failures or attempt to replay a partially failed draw.
        if (_webSolidClipMesh is null)
        {
            _webSolidClipMesh = _device.CreateMesh(new("BEngine.UIElements.WebSolidClipMesh", ReadOnlyMemory<float>.Empty,
                WebSolidClipLayout, usage: GraphicsBufferUsage.Dynamic));
            _webSolidClipUploads = new(_webSolidClipMesh);
        }
        _webSolidClipUploads!.Update(CollectionsMarshal.AsSpan(_webSolidClipVertices));
    }

    private bool TryDrawWebSolidClipRun(List<RenderBatch> batches, ref int index, int width, int height,
        ref IGraphicsProgram? activeProgram, out int vertexCount)
    {
        vertexCount = 0;
        if (index >= _webSolidClipRanges.Count || !_webSolidClipRanges[index].Valid) return false;
        var first = _webSolidClipRanges[index].FirstVertex;
        vertexCount = _webSolidClipRanges[index].VertexCount;
        while (index + 1 < batches.Count && _webSolidClipRanges[index + 1].Valid)
            vertexCount += _webSolidClipRanges[++index].VertexCount;
        if (_webSolidClipShader is null)
        {
            var sources = _webSolidClipSources!.Value;
            _webSolidClipShader = _device.CreateProgram(new("BEngine.UIElements.WebSolidClip", sources.Vertex, sources.Fragment)
                { Uniforms = [new("uViewportWidth", 0, 1), new("uViewportHeight", 4, 1)] });
        }
        _device.SetBlendMode(GraphicsBlendMode.Disabled);
        _device.SetScissor(null);
        if (!ReferenceEquals(activeProgram, _webSolidClipShader))
        { activeProgram = _webSolidClipShader; BindProgram(activeProgram, width, height); }
        _device.Draw(_webSolidClipMesh!, vertexCount, first);
        return true;
    }

    private void DisposeWebSolidClip()
    {
        _webSolidClipUploads?.Dispose(); _webSolidClipMesh?.Dispose(); _webSolidClipShader?.Dispose();
        _webSolidClipVertices.Clear(); _webSolidClipRanges.Clear(); _webSolidClipSources = null;
    }
}
