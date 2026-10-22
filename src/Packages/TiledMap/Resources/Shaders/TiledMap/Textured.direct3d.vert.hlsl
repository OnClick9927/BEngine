cbuffer TilemapViewport : register(b0)
{
    float uViewportWidth;
    float uViewportHeight;
};
struct VertexInput
{
    float2 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};
struct VertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};
VertexOutput main(VertexInput input)
{
    VertexOutput output;
    float2 normalized = float2(input.Position.x / uViewportWidth, input.Position.y / uViewportHeight);
    output.Position = float4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}
