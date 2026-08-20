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
    return UITexture.Sample(UISampler, input.TexCoord) * input.Color;
}
