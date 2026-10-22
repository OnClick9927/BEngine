struct Viewport { width: f32, height: f32 };
@group(0) @binding(0) var<uniform> viewport: Viewport;
@group(0) @binding(1) var postProcessTexture: texture_2d<f32>;
@group(0) @binding(2) var postProcessSampler: sampler;

struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) texCoord: vec2<f32>,
    @location(2) colorParams: vec4<f32>,
    @location(3) colorFilter: vec4<f32>,
    @location(4) vignette: vec4<f32>,
    @location(5) vignetteColor: vec4<f32>,
    @location(6) bloom: vec4<f32>,
    @location(7) textureInfo: vec4<f32>
};
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) texCoord: vec2<f32>,
    @location(1) @interpolate(flat) gradeR: vec4<f32>,
    @location(2) @interpolate(flat) gradeG: vec4<f32>,
    @location(3) @interpolate(flat) vignette: vec4<f32>,
    @location(4) @interpolate(flat) vignetteColor: vec4<f32>,
    @location(5) @interpolate(flat) bloom: vec4<f32>,
    @location(6) @interpolate(flat) textureInfo: vec4<f32>,
    @location(7) @interpolate(flat) gradeB: vec4<f32>
};
fn hueRotate(color: vec3<f32>, degrees: f32) -> vec3<f32> {
    let angle = radians(degrees); let c = cos(angle); let s = sin(angle);
    let matrix = mat3x3<f32>(
        vec3<f32>(0.299 + 0.701*c + 0.168*s, 0.299 - 0.299*c - 0.328*s, 0.299 - 0.300*c + 1.250*s),
        vec3<f32>(0.587 - 0.587*c + 0.330*s, 0.587 + 0.413*c + 0.035*s, 0.587 - 0.588*c - 1.050*s),
        vec3<f32>(0.114 - 0.114*c - 0.497*s, 0.114 - 0.114*c + 0.292*s, 0.114 + 0.886*c - 0.203*s));
    return matrix * color;
}
fn gradeColor(source: vec3<f32>, input: VertexInput) -> vec3<f32> {
    var color = source;
    color *= exp2(vec3<f32>(input.colorParams.x));
    let luma = dot(color, vec3<f32>(0.2126, 0.7152, 0.0722));
    color = mix(vec3<f32>(luma), color, 1.0 + input.colorParams.z);
    color = (color - 0.5) * (1.0 + input.colorParams.y) + 0.5;
    if (input.colorParams.w != 0.0) { color = hueRotate(color, input.colorParams.w); }
    color *= input.colorFilter.rgb;
    return color;
}
@vertex fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    let normalized = vec2<f32>(input.position.x / viewport.width, input.position.y / viewport.height);
    output.position = vec4<f32>(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
    output.texCoord = input.texCoord;
    let bias = gradeColor(vec3<f32>(0.0), input);
    let r = gradeColor(vec3<f32>(1.0,0.0,0.0), input) - bias;
    let g = gradeColor(vec3<f32>(0.0,1.0,0.0), input) - bias;
    let b = gradeColor(vec3<f32>(0.0,0.0,1.0), input) - bias;
    output.gradeR = vec4<f32>(r.x, g.x, b.x, bias.x);
    output.gradeG = vec4<f32>(r.y, g.y, b.y, bias.y);
    output.gradeB = vec4<f32>(r.z, g.z, b.z, bias.z);
    output.vignette = input.vignette;
    output.vignetteColor = input.vignetteColor;
    // Pack fullscreen constants here to avoid repeating them for every pixel.
    output.bloom = vec4<f32>(input.bloom.x * 0.125, input.bloom.y,
        input.textureInfo.x * input.bloom.z, input.textureInfo.y * input.bloom.z);
    output.textureInfo = vec4<f32>(max(0.0, 1.0 - input.vignette.x),
        max(0.001, 1.0 - input.vignette.x + input.vignette.y), input.textureInfo.z, input.textureInfo.w);
    return output;
}
fn brightSample(uv: vec2<f32>, threshold: f32) -> vec3<f32> {
    let value = textureSample(postProcessTexture, postProcessSampler, clamp(uv, vec2<f32>(0.0), vec2<f32>(1.0))).rgb;
    let brightness = max(max(value.r, value.g), value.b);
    return value * (max(brightness - threshold, 0.0) / max(brightness, 0.0001));
}

@fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let source = textureSample(postProcessTexture, postProcessSampler, input.texCoord);
    var color = source.rgb;
    if (/* BENGINE_POST_BLOOM */ input.bloom.x > 0.0 /* BENGINE_POST_ACTIVITY_END */) {
        let d = input.bloom.zw;
        var b = brightSample(input.texCoord + vec2<f32>(d.x, 0.0), input.bloom.y) + brightSample(input.texCoord - vec2<f32>(d.x, 0.0), input.bloom.y);
        b += brightSample(input.texCoord + vec2<f32>(0.0, d.y), input.bloom.y) + brightSample(input.texCoord - vec2<f32>(0.0, d.y), input.bloom.y);
        b += brightSample(input.texCoord + d, input.bloom.y) + brightSample(input.texCoord - d, input.bloom.y);
        b += brightSample(input.texCoord + vec2<f32>(d.x, -d.y), input.bloom.y) + brightSample(input.texCoord + vec2<f32>(-d.x, d.y), input.bloom.y);
        color += b * input.bloom.x;
    }
    let gradedSource = vec4<f32>(color, 1.0);
    color = vec3<f32>(dot(input.gradeR, gradedSource), dot(input.gradeG, gradedSource), dot(input.gradeB, gradedSource));
    let chromaOffset = (input.texCoord * 2.0 - 1.0) * input.vignette.w * 0.01;
    if (/* BENGINE_POST_CHROMA */ input.vignette.w > 0.0 /* BENGINE_POST_ACTIVITY_END */) {
        let separated = vec3<f32>(textureSample(postProcessTexture, postProcessSampler, clamp(input.texCoord + chromaOffset, vec2<f32>(0.0), vec2<f32>(1.0))).r, color.g,
            textureSample(postProcessTexture, postProcessSampler, clamp(input.texCoord - chromaOffset, vec2<f32>(0.0), vec2<f32>(1.0))).b);
        color = mix(color, separated, clamp(input.vignette.w, 0.0, 1.0));
    }
    var centered = input.texCoord * 2.0 - 1.0; centered.x *= input.vignette.z;
    let edge = smoothstep(input.textureInfo.x, input.textureInfo.y, length(centered));
    color = mix(color, input.vignetteColor.rgb, edge * input.vignetteColor.a * select(0.0, 1.0, input.vignette.x > 0.0));
    if (/* BENGINE_POST_GRAIN */ input.textureInfo.w > 0.0 /* BENGINE_POST_ACTIVITY_END */) {
        let grain = fract(sin(dot(input.texCoord * 1000.0, vec2<f32>(12.9898, 78.233))) * 43758.5453) - 0.5;
        color += grain * input.textureInfo.w * 0.08;
    }
    if (input.textureInfo.z > 0.5) { color = select(color, vec3<f32>(0.0), color != color); }
    return vec4<f32>(max(color, vec3<f32>(0.0)), source.a);
}
