cbuffer SceneViewport : register(b0) {
    float uViewportWidth; float uViewportHeight; float2 padding; float4 uMaskSettings;
};
struct VertexInput { float2 Position : POSITION; float4 Color : COLOR0; float2 TexCoord : TEXCOORD0; };
struct FragmentInput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };
FragmentInput main(VertexInput input) {
    FragmentInput output;
    float2 uv = input.Position / float2(uViewportWidth, uViewportHeight);
    output.Position = float4(uv.x * 2 - 1, 1 - uv.y * 2, 0, 1);
    output.TexCoord = input.TexCoord;
    return output;
}
