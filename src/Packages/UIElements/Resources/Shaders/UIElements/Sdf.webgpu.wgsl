struct UIViewport { width: f32, height: f32, threshold: f32 };
@group(0) @binding(0) var<uniform> viewport: UIViewport;
@group(0) @binding(1) var uiTexture: texture_2d<f32>;
@group(0) @binding(2) var uiSampler: sampler;
struct VertexInput
{
    @location(0) position: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texCoord: vec2<f32>
};
struct VertexOutput
{
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texCoord: vec2<f32>
};
@vertex fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    let normalized = vec2<f32>(input.position.x / viewport.width, input.position.y / viewport.height);
    output.position = vec4<f32>(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
    output.color = input.color;
    output.texCoord = input.texCoord;
    return output;
}
@fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32>
{
    let distance = textureSample(uiTexture, uiSampler, input.texCoord).a/*BEngineSdfDistance*/;
    let smoothing = max(fwidth(distance) * 0.5, 0.003922);
    let coverage = smoothstep(viewport.threshold - smoothing, viewport.threshold + smoothing, distance);
    return vec4<f32>(input.color.rgb, input.color.a * coverage);
}
