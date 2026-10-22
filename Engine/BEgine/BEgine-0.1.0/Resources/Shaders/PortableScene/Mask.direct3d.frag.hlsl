cbuffer SceneViewport : register(b0) {
    float uViewportWidth; float uViewportHeight; float2 padding; float4 uMaskSettings;
};
Texture2D SceneTexture : register(t0); SamplerState SceneSampler : register(s0);
Texture2D MaskTexture : register(t1); SamplerState MaskSampler : register(s1);
struct FragmentInput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };
float4 main(FragmentInput input) : SV_TARGET {
    if (uMaskSettings.x < 0.5) {
        float alpha = SceneTexture.Sample(SceneSampler, input.TexCoord).a;
        if (alpha <= 0 || alpha < uMaskSettings.y) discard;
        return float4(1, 1, 1, 1);
    }
    float2 uv = float2(input.TexCoord.x, uMaskSettings.z > 0.5 ? 1 - input.TexCoord.y : input.TexCoord.y);
    float coverage = MaskTexture.Sample(MaskSampler, uv).a;
    if ((uMaskSettings.x < 1.5 && coverage < 0.5) || (uMaskSettings.x > 1.5 && coverage >= 0.5)) discard;
    float4 color = SceneTexture.Sample(SceneSampler, uv);
    if (color.a <= 0) discard;
    return float4(color.rgb / color.a, color.a);
}
