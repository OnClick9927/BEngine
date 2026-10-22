Texture2D SpriteSkinTexture : register(t0);
SamplerState SpriteSkinSampler : register(s0);
struct FragmentInput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};
float4 main(FragmentInput input) : SV_Target0
{
    return SpriteSkinTexture.Sample(SpriteSkinSampler, input.TexCoord) * input.Color;
}
