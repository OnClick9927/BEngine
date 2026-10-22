#version 450
layout(location=0) in vec2 vTexCoord;
layout(set=0,binding=0) uniform SceneViewport { float uViewportWidth; float uViewportHeight; vec4 uMaskSettings; };
layout(set=0,binding=1) uniform texture2D SceneTexture;
layout(set=0,binding=2) uniform sampler SceneSampler;
layout(set=0,binding=3) uniform texture2D MaskTexture;
layout(set=0,binding=4) uniform sampler MaskSampler;
layout(location=0) out vec4 fragColor;
void main() {
    if (uMaskSettings.x < 0.5) {
        float alpha = texture(sampler2D(SceneTexture, SceneSampler), vTexCoord).a;
        if (alpha <= 0.0 || alpha < uMaskSettings.y) discard;
        fragColor = vec4(1.0);
    } else {
        vec2 uv = vec2(vTexCoord.x, uMaskSettings.z > 0.5 ? 1.0-vTexCoord.y : vTexCoord.y);
        float coverage = texture(sampler2D(MaskTexture, MaskSampler), uv).a;
        if ((uMaskSettings.x < 1.5 && coverage < 0.5) || (uMaskSettings.x > 1.5 && coverage >= 0.5)) discard;
        vec4 color = texture(sampler2D(SceneTexture, SceneSampler), uv);
        if (color.a <= 0.0) discard;
        fragColor = vec4(color.rgb / color.a, color.a);
    }
}
