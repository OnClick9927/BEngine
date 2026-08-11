namespace BEngine;

public enum CameraClearFlags
{
    Skybox = 1,
    Color = 2,
    SolidColor = 2,
    Depth = 3,
    Nothing = 4
}

public enum LightType
{
    Spot = 0,
    Directional = 1,
    Point = 2
}

public enum LightShadows
{
    None = 0,
    Hard = 1,
    Soft = 2
}

public enum GlobalIlluminationMode
{
    Disabled = 0,
    Realtime = 1
}

public sealed class Camera : Component
{
    public CameraClearFlags clearFlags { get; set; } = CameraClearFlags.Skybox;
    public Fix64 fieldOfView { get; set; } = 60;
    public Fix64 nearClipPlane { get; set; } = Fix64.Parse("0.1");
    public Fix64 farClipPlane { get; set; } = 1000;
    public Color backgroundColor { get; set; } = new(
        Fix64.Parse("0.055"), Fix64.Parse("0.071"), Fix64.Parse("0.09"), 1);
    public bool isMain { get; set; } = true;
}

[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Skybox")]
public sealed class Skybox : Component
{
    public Color topColor { get; set; } = new(
        Fix64.Parse("0.12"), Fix64.Parse("0.32"), Fix64.Parse("0.68"), 1);
    public Color horizonColor { get; set; } = new(
        Fix64.Parse("0.72"), Fix64.Parse("0.82"), Fix64.Parse("0.92"), 1);
    public Color groundColor { get; set; } = new(
        Fix64.Parse("0.10"), Fix64.Parse("0.12"), Fix64.Parse("0.16"), 1);
    [Range(0, 8)] public Fix64 exposure { get; set; } = Fix64.One;
    [Range(0, 8)] public Fix64 sunIntensity { get; set; } = Fix64.Parse("1.25");
    [Range(0.001f, 1)] public Fix64 sunSize { get; set; } = Fix64.Parse("0.04");
    [Range(0.01f, 1)] public Fix64 horizonThickness { get; set; } = Fix64.Parse("0.32");
}

public static class RenderSettings
{
    public static Material? skybox { get; set; } = CreateDefaultSkybox();
    public static Color ambientLight { get; set; } = new(
        Fix64.Parse("0.22"), Fix64.Parse("0.24"), Fix64.Parse("0.28"), 1);
    public static Fix64 ambientIntensity { get; set; } = Fix64.One;
    public static Color ambientSkyColor { get; set; } = new(
        Fix64.Parse("0.22"), Fix64.Parse("0.32"), Fix64.Parse("0.50"), 1);
    public static Color ambientEquatorColor { get; set; } = new(
        Fix64.Parse("0.20"), Fix64.Parse("0.22"), Fix64.Parse("0.25"), 1);
    public static Color ambientGroundColor { get; set; } = new(
        Fix64.Parse("0.08"), Fix64.Parse("0.09"), Fix64.Parse("0.11"), 1);
    public static GlobalIlluminationMode globalIllumination { get; set; } = GlobalIlluminationMode.Realtime;
    public static Fix64 indirectIntensity { get; set; } = Fix64.One;

    private static Material CreateDefaultSkybox()
    {
        var material = new Material(Shader.Find("BEngine/Procedural Skybox")) { name = "Default Skybox" };
        material.SetColor("_TopColor", new Color(
            Fix64.Parse("0.12"), Fix64.Parse("0.32"), Fix64.Parse("0.68"), 1));
        material.SetColor("_HorizonColor", new Color(
            Fix64.Parse("0.72"), Fix64.Parse("0.82"), Fix64.Parse("0.92"), 1));
        material.SetColor("_GroundColor", new Color(
            Fix64.Parse("0.10"), Fix64.Parse("0.12"), Fix64.Parse("0.16"), 1));
        material.SetFloat("_Exposure", Fix64.One);
        material.SetFloat("_SunIntensity", Fix64.Parse("1.25"));
        material.SetFloat("_SunSize", Fix64.Parse("0.04"));
        material.SetFloat("_HorizonThickness", Fix64.Parse("0.32"));
        return material;
    }
}

public sealed class MeshRenderer : Component
{
    private readonly Material _material = new(Shader.Find("BEngine/Lit"));
    public string mesh { get; set; } = "Cube";
    public Material material => _material;
    public Color color
    {
        get => material.color;
        set => material.color = value;
    }
    [Range(0, 1)] public Fix64 metallic
    {
        get => material.GetFloat("_Metallic", Fix64.Zero);
        set => material.SetFloat("_Metallic", Fix64.Clamp(value, Fix64.Zero, Fix64.One));
    }
    [Range(0, 1)] public Fix64 smoothness
    {
        get => material.GetFloat("_Smoothness", Fix64.Parse("0.35"));
        set => material.SetFloat("_Smoothness", Fix64.Clamp(value, Fix64.Zero, Fix64.One));
    }
    public Color emissionColor
    {
        get => material.GetColor("_EmissionColor", Color.black);
        set => material.SetColor("_EmissionColor", value);
    }
    [Range(0, 16)] public Fix64 emissionIntensity
    {
        get => material.GetFloat("_EmissionIntensity", Fix64.Zero);
        set => material.SetFloat("_EmissionIntensity", Fix64.Max(Fix64.Zero, value));
    }
    public bool castShadows { get; set; } = true;
    public bool receiveShadows { get; set; } = true;
    public bool contributeGlobalIllumination { get; set; } = true;
    public bool receiveGlobalIllumination { get; set; } = true;
    [Range(0, 8)] public Fix64 giContribution { get; set; } = Fix64.One;

    public MeshRenderer()
    {
        color = new Color(Fix64.Parse("0.15"), Fix64.Parse("0.72"), Fix64.Parse("0.63"), 1);
        metallic = Fix64.Zero;
        smoothness = Fix64.Parse("0.35");
        emissionColor = Color.black;
        emissionIntensity = Fix64.Zero;
    }
}

[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Light")]
public class Light : Behaviour
{
    public LightType type { get; set; } = LightType.Point;
    public Color color { get; set; } = Color.white;
    public Fix64 intensity { get; set; } = Fix64.One;
    [Range(0.01f, 10000)] public Fix64 range { get; set; } = 10;
    [Range(1, 179)] public Fix64 spotAngle { get; set; } = 30;
    [Range(0, 179)] public Fix64 innerSpotAngle { get; set; } = 21;
    public LightShadows shadows { get; set; } = LightShadows.None;
    [Range(0, 1)] public Fix64 shadowStrength { get; set; } = Fix64.One;
    [Range(0, 0.1f)] public Fix64 shadowBias { get; set; } = Fix64.Parse("0.003");
    [Range(0, 8)] public Fix64 bounceIntensity { get; set; } = Fix64.One;
}

[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Directional Light")]
public sealed class DirectionalLight : Light
{
    public DirectionalLight()
    {
        type = LightType.Directional;
        shadows = LightShadows.Soft;
    }
}

[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Lighting Settings")]
public sealed class LightingSettings : Component
{
    public Color ambientSkyColor { get; set; } = RenderSettings.ambientSkyColor;
    public Color ambientEquatorColor { get; set; } = RenderSettings.ambientEquatorColor;
    public Color ambientGroundColor { get; set; } = RenderSettings.ambientGroundColor;
    [Range(0, 8)] public Fix64 ambientIntensity { get; set; } = Fix64.One;
    public GlobalIlluminationMode globalIllumination { get; set; } = GlobalIlluminationMode.Realtime;
    [Range(0, 8)] public Fix64 indirectIntensity { get; set; } = Fix64.One;
    [Range(0.1f, 1000)] public Fix64 bounceDistance { get; set; } = 8;
    [Range(1, 8)] public int maxRealtimeLights { get; set; } = 8;
    public bool enableShadows { get; set; } = true;
    [Range(128, 4096)] public int shadowResolution { get; set; } = 1024;
    [Range(1, 1000)] public Fix64 shadowDistance { get; set; } = 50;
}

public sealed class MissingComponent : Component
{
    public string originalType { get; internal set; } = string.Empty;
    public Dictionary<string, string> serializedFields { get; internal set; } = [];
}
