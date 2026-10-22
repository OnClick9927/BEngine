Texture2D TilemapTexture : register(t0);
SamplerState TilemapSampler : register(s0);
struct FragmentInput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};
float4 main(FragmentInput input) : SV_Target0
{
    return TilemapTexture.Sample(TilemapSampler, input.TexCoord) * input.Color;
}
