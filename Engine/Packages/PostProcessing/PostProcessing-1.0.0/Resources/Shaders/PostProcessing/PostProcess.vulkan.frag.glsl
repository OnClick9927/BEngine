#version 450
layout(location = 0) in vec2 vTexCoord;
layout(location = 1) flat in vec4 vGradeR;
layout(location = 2) flat in vec4 vGradeG;
layout(location = 3) flat in vec4 vVignette;
layout(location = 4) flat in vec4 vVignetteColor;
layout(location = 5) flat in vec4 vBloom;
layout(location = 6) flat in vec4 vTextureInfo;
layout(set = 0, binding = 1) uniform texture2D PostProcessTexture;
layout(set = 0, binding = 2) uniform sampler PostProcessSampler;
layout(location = 7) flat in vec4 vGradeB;
layout(location = 0) out vec4 fragColor;

vec3 readColor(vec2 uv) { return texture(sampler2D(PostProcessTexture, PostProcessSampler), clamp(uv, 0.0, 1.0)).rgb; }
vec3 brightSample(vec2 uv)
{
    vec3 value = readColor(uv);
    float brightness = max(max(value.r, value.g), value.b);
    return value * (max(brightness - vBloom.y, 0.0) / max(brightness, 0.0001));
}
void main()
{
    vec4 source = texture(sampler2D(PostProcessTexture, PostProcessSampler), vTexCoord);
    vec3 color = source.rgb;
    if (/* BENGINE_POST_BLOOM */ vBloom.x > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        vec2 d = vBloom.zw;
        vec3 b = brightSample(vTexCoord + vec2(d.x, 0)) + brightSample(vTexCoord - vec2(d.x, 0));
        b += brightSample(vTexCoord + vec2(0, d.y)) + brightSample(vTexCoord - vec2(0, d.y));
        b += brightSample(vTexCoord + d) + brightSample(vTexCoord - d);
        b += brightSample(vTexCoord + vec2(d.x, -d.y)) + brightSample(vTexCoord + vec2(-d.x, d.y));
        color += b * vBloom.x;
    }
    vec4 gradedSource = vec4(color, 1);
    color = vec3(dot(vGradeR, gradedSource), dot(vGradeG, gradedSource), dot(vGradeB, gradedSource));
    vec2 chromaOffset = (vTexCoord * 2.0 - 1.0) * vVignette.w * 0.01;
    if (/* BENGINE_POST_CHROMA */ vVignette.w > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        vec3 separated = vec3(readColor(vTexCoord + chromaOffset).r, color.g, readColor(vTexCoord - chromaOffset).b);
        color = mix(color, separated, clamp(vVignette.w, 0.0, 1.0));
    }
    vec2 centered = vTexCoord * 2.0 - 1.0; centered.x *= vVignette.z;
    float edge = smoothstep(vTextureInfo.x, vTextureInfo.y, length(centered));
    color = mix(color, vVignetteColor.rgb, edge * vVignetteColor.a * float(vVignette.x > 0.0));
    if (/* BENGINE_POST_GRAIN */ vTextureInfo.w > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        float grain = fract(sin(dot(vTexCoord * 1000.0, vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
        color += grain * vTextureInfo.w * 0.08;
    }
    if (vTextureInfo.z > 0.5 && any(isnan(color))) color = vec3(0);
    fragColor = vec4(max(color, 0), source.a);
}
