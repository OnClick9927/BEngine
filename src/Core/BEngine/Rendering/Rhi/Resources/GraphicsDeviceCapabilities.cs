namespace BEngine.Rendering.Rhi;

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
