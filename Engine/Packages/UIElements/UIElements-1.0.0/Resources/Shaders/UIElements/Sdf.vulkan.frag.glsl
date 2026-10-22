#version 450
layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vTexCoord;
layout(set = 0, binding = 1) uniform texture2D UITexture;
layout(set = 0, binding = 2) uniform sampler UISampler;
layout(set = 0, binding = 0) uniform UIViewport {
    float uViewportWidth; float uViewportHeight; float uSdfThreshold;
};
layout(location = 0) out vec4 fragColor;
void main() {
    float distance = texture(sampler2D(UITexture, UISampler), vTexCoord).a/*BEngineSdfDistance*/;
    float smoothing = max(fwidth(distance) * 0.5, 0.003922);
    float coverage = smoothstep(uSdfThreshold - smoothing, uSdfThreshold + smoothing, distance);
    fragColor = vec4(vColor.rgb, vColor.a * coverage);
}
