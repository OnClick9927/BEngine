Texture2D PostProcessTexture : register(t0);
SamplerState PostProcessSampler : register(s0);
struct FragmentInput
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
float3 brightSample(float2 uv, float threshold)
{
    float3 value = PostProcessTexture.Sample(PostProcessSampler, saturate(uv)).rgb;
    float brightness = max(max(value.r, value.g), value.b);
    return value * (max(brightness - threshold, 0.0) / max(brightness, 0.0001));
}
float4 main(FragmentInput input) : SV_Target0
{
    float4 source = PostProcessTexture.Sample(PostProcessSampler, input.TexCoord);
    float3 color = source.rgb;
    if (/* BENGINE_POST_BLOOM */ input.Bloom.x > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        float2 d = input.Bloom.zw;
        float3 b = brightSample(input.TexCoord + float2(d.x, 0), input.Bloom.y) + brightSample(input.TexCoord - float2(d.x, 0), input.Bloom.y);
        b += brightSample(input.TexCoord + float2(0, d.y), input.Bloom.y) + brightSample(input.TexCoord - float2(0, d.y), input.Bloom.y);
        b += brightSample(input.TexCoord + d, input.Bloom.y) + brightSample(input.TexCoord - d, input.Bloom.y);
        b += brightSample(input.TexCoord + float2(d.x, -d.y), input.Bloom.y) + brightSample(input.TexCoord + float2(-d.x, d.y), input.Bloom.y);
        color += b * input.Bloom.x;
    }
    float4 gradedSource = float4(color, 1);
    color = float3(dot(input.GradeR, gradedSource), dot(input.GradeG, gradedSource), dot(input.GradeB, gradedSource));
    float2 chromaOffset = (input.TexCoord * 2.0 - 1.0) * input.Vignette.w * 0.01;
    if (/* BENGINE_POST_CHROMA */ input.Vignette.w > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        float3 separated = float3(PostProcessTexture.Sample(PostProcessSampler, saturate(input.TexCoord + chromaOffset)).r, color.g,
            PostProcessTexture.Sample(PostProcessSampler, saturate(input.TexCoord - chromaOffset)).b);
        color = lerp(color, separated, saturate(input.Vignette.w));
    }
    float2 centered = input.TexCoord * 2.0 - 1.0; centered.x *= input.Vignette.z;
    float edge = smoothstep(input.TextureInfo.x, input.TextureInfo.y, length(centered));
    color = lerp(color, input.VignetteColor.rgb, edge * input.VignetteColor.a * (input.Vignette.x > 0.0 ? 1.0 : 0.0));
    if (/* BENGINE_POST_GRAIN */ input.TextureInfo.w > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        float grain = frac(sin(dot(input.TexCoord * 1000.0, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
        color += grain * input.TextureInfo.w * 0.08;
    }
    if (input.TextureInfo.z > 0.5 && any(isnan(color))) color = 0.0;
    return float4(max(color, 0.0), source.a);
}
