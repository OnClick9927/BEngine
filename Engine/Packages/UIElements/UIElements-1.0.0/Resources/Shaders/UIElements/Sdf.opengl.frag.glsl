#version 330 core
in vec4 vColor;
in vec2 vTexCoord;
uniform sampler2D uTexture;
uniform float uSdfThreshold;
out vec4 fragColor;
void main() {
    float distance = texture(uTexture, vTexCoord).a/*BEngineSdfDistance*/;
    float smoothing = max(fwidth(distance) * 0.5, 0.003922);
    float coverage = smoothstep(uSdfThreshold - smoothing, uSdfThreshold + smoothing, distance);
    fragColor = vec4(vColor.rgb, vColor.a * coverage);
}
