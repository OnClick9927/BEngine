#version 330 core
in vec4 vColor;
in vec2 vTexCoord;
in vec2 vPosition;
out vec4 oColor;
uniform vec4 uLighting[__LIGHTING_CAPACITY__];
uniform sampler2D uTexture;
vec4 sampleTexture() { return texture(uTexture, vTexCoord); }
uniform sampler2D uNormal;
uniform sampler2D uCookie0;
uniform sampler2D uCookie1;
uniform sampler2D uCookie2;
uniform sampler2D uCookie3;
uniform sampler2D uCookie4;
uniform sampler2D uCookie5;
vec3 sampleNormal(vec2 uv) { return texture(uNormal, uv).rgb; }
vec4 sampleCookie(int index, vec2 uv) {
    if (index == 0) return texture(uCookie0, uv);
    if (index == 1) return texture(uCookie1, uv);
    if (index == 2) return texture(uCookie2, uv);
    if (index == 3) return texture(uCookie3, uv);
    if (index == 4) return texture(uCookie4, uv);
    return texture(uCookie5, uv);
}
