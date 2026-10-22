struct SceneViewport { width: f32, height: f32, settings: vec4<f32> };
@group(0) @binding(0) var<uniform> viewport: SceneViewport;
@group(0) @binding(1) var sceneTexture: texture_2d<f32>;
@group(0) @binding(2) var sceneSampler: sampler;
@group(0) @binding(3) var maskTexture: texture_2d<f32>;
@group(0) @binding(4) var maskSampler: sampler;
struct VertexInput { @location(0) position: vec2<f32>, @location(1) color: vec4<f32>, @location(2) texCoord: vec2<f32> };
struct VertexOutput { @builtin(position) position: vec4<f32>, @location(0) texCoord: vec2<f32> };
@vertex fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    let uv = input.position / vec2<f32>(viewport.width, viewport.height);
    output.position = vec4<f32>(uv.x * 2 - 1, 1 - uv.y * 2, 0, 1);
    output.texCoord = input.texCoord;
    return output;
}
@fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // Derivatives must be evaluated outside the mode-dependent branches.
    let uv = vec2<f32>(input.texCoord.x, select(input.texCoord.y, 1 - input.texCoord.y, viewport.settings.z > 0.5));
    let source = textureSample(sceneTexture, sceneSampler, input.texCoord);
    let content = textureSample(sceneTexture, sceneSampler, uv);
    let coverage = textureSample(maskTexture, maskSampler, uv).a;
    if (viewport.settings.x < 0.5) {
        if (source.a <= 0 || source.a < viewport.settings.y) { discard; }
        return vec4<f32>(1);
    }
    if ((viewport.settings.x < 1.5 && coverage < 0.5) || (viewport.settings.x > 1.5 && coverage >= 0.5) || content.a <= 0) { discard; }
    return vec4<f32>(content.rgb / content.a, content.a);
}
