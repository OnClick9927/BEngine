cbuffer UIViewport : register(b0) { float uViewportWidth; float uViewportHeight; float uSdfThreshold; };
Texture2D UITexture : register(t0);
SamplerState UISampler : register(s0);
struct FragmentInput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};
float4 main(FragmentInput input) : SV_Target0
{
    float distance = UITexture.Sample(UISampler, input.TexCoord).a/*BEngineSdfDistance*/;
    float smoothing = max(fwidth(distance) * 0.5, 0.003922);
    float coverage = smoothstep(uSdfThreshold - smoothing, uSdfThreshold + smoothing, distance);
    return float4(input.Color.rgb, input.Color.a * coverage);
}
