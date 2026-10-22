#version 330 core
in vec2 vTexCoord;
uniform sampler2D uTexture;
uniform sampler2D uMaskTexture;
uniform vec4 uMaskSettings;
out vec4 fragColor;
void main() {
    if (uMaskSettings.x < 0.5) {
        float alpha = texture(uTexture, vTexCoord).a;
        if (alpha <= 0.0 || alpha < uMaskSettings.y) discard;
        fragColor = vec4(1.0);
    } else {
        vec2 uv = vec2(vTexCoord.x, uMaskSettings.z > 0.5 ? 1.0-vTexCoord.y : vTexCoord.y);
        float coverage = texture(uMaskTexture, uv).a;
        if ((uMaskSettings.x < 1.5 && coverage < 0.5) || (uMaskSettings.x > 1.5 && coverage >= 0.5)) discard;
        vec4 color = texture(uTexture, uv);
        if (color.a <= 0.0) discard;
        fragColor = vec4(color.rgb / color.a, color.a);
    }
}
