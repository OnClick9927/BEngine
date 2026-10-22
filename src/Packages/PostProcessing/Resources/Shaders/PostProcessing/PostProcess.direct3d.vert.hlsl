cbuffer PostProcessViewport : register(b0)
{
    float uViewportWidth;
    float uViewportHeight;
};
struct VertexInput
{
    float2 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
    float4 ColorParams : TEXCOORD1;
    float4 ColorFilter : TEXCOORD2;
    float4 Vignette : TEXCOORD3;
    float4 VignetteColor : TEXCOORD4;
    float4 Bloom : TEXCOORD5;
    float4 TextureInfo : TEXCOORD6;
};
struct VertexOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    nointerpolation float4 GradeR : TEXCOORD1;
    nointerpolation float4 GradeG : TEXCOORD2;
    nointerpolation float4 Vignette : TEXCOORD3;
    nointerpolation float4 VignetteColor : TEXCOORD4;
    nointerpolation float4 Bloom : TEXCOORD5;
    nointerpolation float4 TextureInfo : TEXCOORD6;
    nointerpolation float4 GradeB : TEXCOORD7;
};
float3 hueRotate(float3 color, float degrees)
{
    float angle = radians(degrees), c = cos(angle), s = sin(angle);
    float3x3 hueMatrix = float3x3(
        0.299 + 0.701*c + 0.168*s, 0.587 - 0.587*c + 0.330*s, 0.114 - 0.114*c - 0.497*s,
        0.299 - 0.299*c - 0.328*s, 0.587 + 0.413*c + 0.035*s, 0.114 - 0.114*c + 0.292*s,
        0.299 - 0.300*c + 1.250*s, 0.587 - 0.588*c - 1.050*s, 0.114 + 0.886*c - 0.203*s);
    return mul(hueMatrix, color);
}
float3 gradeColor(float3 color, VertexInput input)
{
    color *= exp2(input.ColorParams.x);
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    color = lerp(luma.xxx, color, 1.0 + input.ColorParams.z);
    color = (color - 0.5) * (1.0 + input.ColorParams.y) + 0.5;
    if (input.ColorParams.w != 0.0) color = hueRotate(color, input.ColorParams.w);
    color *= input.ColorFilter.rgb;
    return color;
}
VertexOutput main(VertexInput input)
{
    VertexOutput output;
    float2 normalized = float2(input.Position.x / uViewportWidth, input.Position.y / uViewportHeight);
    output.Position = float4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
    output.TexCoord = input.TexCoord;
    float3 bias = gradeColor(float3(0,0,0), input);
    float3 r = gradeColor(float3(1,0,0), input) - bias;
    float3 g = gradeColor(float3(0,1,0), input) - bias;
    float3 b = gradeColor(float3(0,0,1), input) - bias;
    output.GradeR = float4(r.x, g.x, b.x, bias.x);
    output.GradeG = float4(r.y, g.y, b.y, bias.y);
    output.GradeB = float4(r.z, g.z, b.z, bias.z);
    output.Vignette = input.Vignette;
    output.VignetteColor = input.VignetteColor;
    // Pack fullscreen constants here to avoid repeating them for every pixel.
    output.Bloom = float4(input.Bloom.x * 0.125, input.Bloom.y,
        input.TextureInfo.x * input.Bloom.z, input.TextureInfo.y * input.Bloom.z);
    output.TextureInfo = float4(max(0.0, 1.0 - input.Vignette.x),
        max(0.001, 1.0 - input.Vignette.x + input.Vignette.y), input.TextureInfo.z, input.TextureInfo.w);
    return output;
}
