namespace BEngine.Rendering.Rhi;

public enum GraphicsBackend
{
    OpenGL,
    Direct3D11,
    Direct3D12,
    Vulkan,
    WebGPU
}

public enum GraphicsBackendAvailability
{
    Available,
    Unavailable,
    NotImplemented
}

[Flags]
public enum GraphicsDeviceFeatures
{
    None = 0,
    Rasterization = 1 << 0,
    ShaderPrograms = 1 << 1,
    StaticVertexBuffers = 1 << 2,
    DynamicVertexBuffers = 1 << 3,
    SampledTextures = 1 << 4,
    RenderTargets = 1 << 5,
    DepthTextures = 1 << 6,
    AlphaBlending = 1 << 7,
    ScissorRectangles = 1 << 8,
    DepthBias = 1 << 9
}

public enum GraphicsShaderLanguage
{
    Glsl,
    Hlsl,
    SpirV,
    Wgsl
}

public enum GraphicsShaderStage
{
    Vertex,
    Fragment
}

public enum GraphicsPrimitiveTopology
{
    TriangleList,
    LineList
}

public enum GraphicsBufferUsage
{
    Static,
    Dynamic
}

public enum GraphicsTextureFormat
{
    R8Unorm,
    Rgba8Unorm,
    Depth24Unorm,
    Depth24Stencil8
}

[Flags]
public enum GraphicsTextureUsage
{
    None = 0,
    Sampled = 1 << 0,
    RenderTarget = 1 << 1
}

public enum GraphicsTextureFilter
{
    Nearest,
    Linear
}

public enum GraphicsTextureAddressMode
{
    ClampToEdge,
    Repeat
}

[Flags]
public enum GraphicsClearFlags
{
    None = 0,
    Color = 1 << 0,
    Depth = 1 << 1,
    Stencil = 1 << 2
}

public enum GraphicsBlendMode
{
    Disabled,
    AlphaBlend
}

public readonly record struct GraphicsBackendSupport(
    GraphicsBackend Backend,
    GraphicsBackendAvailability Availability,
    string Reason)
{
    public bool CanCreateDevice => Availability == GraphicsBackendAvailability.Available;
}

public sealed record GraphicsBackendDescriptor(
    GraphicsBackend Backend,
    string DisplayName,
    bool HasBuiltInProvider,
    IReadOnlyList<GraphicsShaderLanguage> ShaderLanguages);

public static class GraphicsBackendCatalog
{
    private static readonly IReadOnlyList<GraphicsBackendDescriptor> Descriptors =
        Array.AsReadOnly<GraphicsBackendDescriptor>(
    [
        new(GraphicsBackend.OpenGL, "OpenGL", true, [GraphicsShaderLanguage.Glsl]),
        new(GraphicsBackend.Direct3D11, "Direct3D 11", false, [GraphicsShaderLanguage.Hlsl]),
        new(GraphicsBackend.Direct3D12, "Direct3D 12", false, [GraphicsShaderLanguage.Hlsl]),
        new(GraphicsBackend.Vulkan, "Vulkan", false,
            [GraphicsShaderLanguage.SpirV, GraphicsShaderLanguage.Glsl]),
        new(GraphicsBackend.WebGPU, "WebGPU", false, [GraphicsShaderLanguage.Wgsl])
    ]);

    public static IReadOnlyList<GraphicsBackendDescriptor> All => Descriptors;

    public static GraphicsBackendDescriptor Get(GraphicsBackend backend) =>
        Descriptors.First(item => item.Backend == backend);
}

public sealed class GraphicsDeviceCapabilities
{
    private readonly IReadOnlyList<GraphicsShaderLanguage> _shaderLanguages;

    public GraphicsBackend Backend { get; }
    public string DeviceName { get; }
    public string ApiVersion { get; }
    public GraphicsDeviceFeatures Features { get; }
    public IReadOnlyList<GraphicsShaderLanguage> ShaderLanguages => _shaderLanguages;

    public GraphicsDeviceCapabilities(
        GraphicsBackend backend,
        string deviceName,
        string apiVersion,
        GraphicsDeviceFeatures features,
        IEnumerable<GraphicsShaderLanguage> shaderLanguages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);
        ArgumentNullException.ThrowIfNull(shaderLanguages);
        Backend = backend;
        DeviceName = deviceName;
        ApiVersion = apiVersion;
        Features = features;
        _shaderLanguages = Array.AsReadOnly(shaderLanguages.Distinct().ToArray());
    }

    public bool Supports(GraphicsDeviceFeatures features) => (Features & features) == features;

    public bool Supports(GraphicsShaderLanguage language) => _shaderLanguages.Contains(language);

    public void Require(GraphicsDeviceFeatures features)
    {
        var missing = features & ~Features;
        if (missing != GraphicsDeviceFeatures.None)
        {
            throw new NotSupportedException($"{Backend} device '{DeviceName}' does not support: {missing}.");
        }
    }
}

public readonly record struct GraphicsVertexAttribute(int Location, int ComponentCount, int OffsetBytes)
{
    internal void Validate(int strideBytes)
    {
        if (Location < 0) throw new ArgumentOutOfRangeException(nameof(Location));
        if (ComponentCount is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(ComponentCount));
        if (OffsetBytes < 0 || OffsetBytes + ComponentCount * sizeof(float) > strideBytes)
            throw new ArgumentOutOfRangeException(nameof(OffsetBytes));
    }
}

public sealed class GraphicsVertexLayout
{
    private readonly IReadOnlyList<GraphicsVertexAttribute> _attributes;

    public int StrideBytes { get; }
    public IReadOnlyList<GraphicsVertexAttribute> Attributes => _attributes;

    public GraphicsVertexLayout(int strideBytes, IEnumerable<GraphicsVertexAttribute> attributes)
    {
        if (strideBytes <= 0 || strideBytes % sizeof(float) != 0)
            throw new ArgumentOutOfRangeException(nameof(strideBytes));
        ArgumentNullException.ThrowIfNull(attributes);
        var copiedAttributes = attributes.ToArray();
        if (copiedAttributes.Length == 0) throw new ArgumentException("A vertex layout requires attributes.", nameof(attributes));
        foreach (var attribute in copiedAttributes) attribute.Validate(strideBytes);
        if (copiedAttributes.Select(item => item.Location).Distinct().Count() != copiedAttributes.Length)
            throw new ArgumentException("Vertex attribute locations must be unique.", nameof(attributes));
        StrideBytes = strideBytes;
        _attributes = Array.AsReadOnly(copiedAttributes);
    }
}

public sealed class GraphicsMeshDescription
{
    public string Label { get; }
    public ReadOnlyMemory<float> Vertices { get; }
    public GraphicsVertexLayout Layout { get; }
    public GraphicsPrimitiveTopology Topology { get; }
    public GraphicsBufferUsage Usage { get; }
    public int VertexCount => Vertices.Length * sizeof(float) / Layout.StrideBytes;

    public GraphicsMeshDescription(
        string label,
        ReadOnlyMemory<float> vertices,
        GraphicsVertexLayout layout,
        GraphicsPrimitiveTopology topology = GraphicsPrimitiveTopology.TriangleList,
        GraphicsBufferUsage usage = GraphicsBufferUsage.Static)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(layout);
        if (vertices.Length * sizeof(float) % layout.StrideBytes != 0)
            throw new ArgumentException("Vertex data length must be a multiple of the layout stride.", nameof(vertices));
        Label = label;
        Vertices = vertices;
        Layout = layout;
        Topology = topology;
        Usage = usage;
    }
}

public readonly record struct GraphicsTextureDescription(
    int Width,
    int Height,
    GraphicsTextureFormat Format,
    GraphicsTextureUsage Usage,
    GraphicsTextureFilter MinFilter = GraphicsTextureFilter.Linear,
    GraphicsTextureFilter MagFilter = GraphicsTextureFilter.Linear,
    GraphicsTextureAddressMode AddressMode = GraphicsTextureAddressMode.ClampToEdge)
{
    internal void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        if (Usage == GraphicsTextureUsage.None) throw new ArgumentOutOfRangeException(nameof(Usage));
    }
}

public readonly record struct GraphicsRenderTargetDescription(
    int Width,
    int Height,
    GraphicsTextureFormat? ColorFormat,
    GraphicsTextureFormat? DepthFormat,
    bool SampleColor = false,
    bool SampleDepth = false,
    GraphicsTextureFilter Filter = GraphicsTextureFilter.Nearest)
{
    internal void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        if (ColorFormat is null && DepthFormat is null)
            throw new ArgumentException("A render target requires a color or depth attachment.");
        if (ColorFormat is GraphicsTextureFormat.Depth24Unorm or GraphicsTextureFormat.Depth24Stencil8)
            throw new ArgumentException("The color attachment must use a color format.", nameof(ColorFormat));
        if (DepthFormat is not null and not GraphicsTextureFormat.Depth24Unorm and not GraphicsTextureFormat.Depth24Stencil8)
            throw new ArgumentException("The depth attachment must use a depth format.", nameof(DepthFormat));
    }
}

public readonly record struct GraphicsRect(int X, int Y, int Width, int Height)
{
    internal void Validate()
    {
        if (Width < 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height < 0) throw new ArgumentOutOfRangeException(nameof(Height));
    }
}

public readonly record struct GraphicsDepthState(bool TestEnabled, bool WriteEnabled)
{
    public static GraphicsDepthState Disabled => new(false, false);
    public static GraphicsDepthState Default => new(true, true);
}

public readonly record struct GraphicsRasterizerState(bool DepthBiasEnabled, float SlopeScale, float ConstantBias)
{
    public static GraphicsRasterizerState Default => new(false, 0, 0);
    public static GraphicsRasterizerState WithDepthBias(float slopeScale, float constantBias) =>
        new(true, slopeScale, constantBias);
}

public readonly record struct GraphicsShaderSource(
    GraphicsShaderStage Stage,
    GraphicsShaderLanguage Language,
    string Code,
    string EntryPoint = "main");

public sealed record GraphicsShaderProgramDescription(
    string Label,
    GraphicsShaderSource VertexShader,
    GraphicsShaderSource FragmentShader)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);
        if (VertexShader.Stage != GraphicsShaderStage.Vertex)
            throw new ArgumentException("VertexShader must contain a vertex stage.", nameof(VertexShader));
        if (FragmentShader.Stage != GraphicsShaderStage.Fragment)
            throw new ArgumentException("FragmentShader must contain a fragment stage.", nameof(FragmentShader));
        if (VertexShader.Language != FragmentShader.Language)
            throw new ArgumentException("Both shader stages must use the same source language.");
        ArgumentException.ThrowIfNullOrWhiteSpace(VertexShader.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(FragmentShader.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(VertexShader.EntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(FragmentShader.EntryPoint);
    }
}
