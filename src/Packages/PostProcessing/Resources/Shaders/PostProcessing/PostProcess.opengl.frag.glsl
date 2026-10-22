#version 330 core
in vec2 vTexCoord;
flat in vec4 vGradeR;
flat in vec4 vGradeG;
flat in vec4 vVignette;
flat in vec4 vVignetteColor;
flat in vec4 vBloom;
flat in vec4 vTextureInfo;
uniform sampler2D uTexture;
flat in vec4 vGradeB;
out vec4 fragColor;


vec3 brightSample(vec2 uv)
{
    vec3 sampleColor = texture(uTexture, clamp(uv, 0.0, 1.0)).rgb;
    float brightness = max(max(sampleColor.r, sampleColor.g), sampleColor.b);
    return sampleColor * (max(brightness - vBloom.y, 0.0) / max(brightness, 0.0001));
}

void main()
{
    vec4 source = texture(uTexture, vTexCoord);
    vec3 color = source.rgb;
    if (/* BENGINE_POST_BLOOM */ vBloom.x > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        vec2 stepSize = vBloom.zw;
        vec3 bloom = brightSample(vTexCoord + vec2(stepSize.x, 0.0));
        bloom += brightSample(vTexCoord - vec2(stepSize.x, 0.0));
        bloom += brightSample(vTexCoord + vec2(0.0, stepSize.y));
        bloom += brightSample(vTexCoord - vec2(0.0, stepSize.y));
        bloom += brightSample(vTexCoord + stepSize);
        bloom += brightSample(vTexCoord - stepSize);
        bloom += brightSample(vTexCoord + vec2(stepSize.x, -stepSize.y));
        bloom += brightSample(vTexCoord + vec2(-stepSize.x, stepSize.y));
        color += bloom * vBloom.x;
    }
    vec4 gradedSource = vec4(color, 1);
    color = vec3(dot(vGradeR, gradedSource), dot(vGradeG, gradedSource), dot(vGradeB, gradedSource));
    vec2 chromaOffset = (vTexCoord * 2.0 - 1.0) * vVignette.w * 0.01;
    if (/* BENGINE_POST_CHROMA */ vVignette.w > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        vec3 separated = vec3(texture(uTexture, clamp(vTexCoord + chromaOffset, 0.0, 1.0)).r,
            color.g, texture(uTexture, clamp(vTexCoord - chromaOffset, 0.0, 1.0)).b);
        color = mix(color, separated, clamp(vVignette.w, 0.0, 1.0));
    }
    vec2 centered = vTexCoord * 2.0 - 1.0;
    centered.x *= vVignette.z;
    float vignette = smoothstep(vTextureInfo.x, vTextureInfo.y, length(centered));
    color = mix(color, vVignetteColor.rgb, vignette * vVignetteColor.a * float(vVignette.x > 0.0));
    if (/* BENGINE_POST_GRAIN */ vTextureInfo.w > 0.0 /* BENGINE_POST_ACTIVITY_END */)
    {
        float grain = fract(sin(dot(vTexCoord * 1000.0, vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
        color += grain * vTextureInfo.w * 0.08;
    }
    if (vTextureInfo.z > 0.5 && any(isnan(color))) color = vec3(0.0);
    fragColor = vec4(max(color, 0.0), source.a);
}
