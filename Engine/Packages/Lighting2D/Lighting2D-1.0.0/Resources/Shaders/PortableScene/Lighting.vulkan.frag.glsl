#version 450
layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vTexCoord;
layout(location = 2) in vec2 vPosition;
layout(location = 0) out vec4 oColor;
layout(set = 0, binding = 0) uniform SceneLighting
{
    float uViewportWidth;
    float uViewportHeight;
    vec2 padding;
    vec4 uLighting[__LIGHTING_CAPACITY__];
};
layout(set = 0, binding = 1) uniform texture2D SceneTexture;
layout(set = 0, binding = 2) uniform sampler SceneSampler;
vec4 sampleTexture() { return texture(sampler2D(SceneTexture, SceneSampler), vTexCoord); }
layout(set=0,binding=3) uniform texture2D NormalTexture;
layout(set=0,binding=4) uniform sampler NormalSampler;
layout(set=0,binding=5) uniform texture2D Cookie0;
layout(set=0,binding=6) uniform sampler CookieSampler0;
layout(set=0,binding=7) uniform texture2D Cookie1;
layout(set=0,binding=8) uniform sampler CookieSampler1;
layout(set=0,binding=9) uniform texture2D Cookie2;
layout(set=0,binding=10) uniform sampler CookieSampler2;
layout(set=0,binding=11) uniform texture2D Cookie3;
layout(set=0,binding=12) uniform sampler CookieSampler3;
layout(set=0,binding=13) uniform texture2D Cookie4;
layout(set=0,binding=14) uniform sampler CookieSampler4;
layout(set=0,binding=15) uniform texture2D Cookie5;
layout(set=0,binding=16) uniform sampler CookieSampler5;
vec3 sampleNormal(vec2 uv) { return texture(sampler2D(NormalTexture, NormalSampler), uv).rgb; }
vec4 sampleCookie(int index, vec2 uv) {
    if (index == 0) return texture(sampler2D(Cookie0, CookieSampler0), uv);
    if (index == 1) return texture(sampler2D(Cookie1, CookieSampler1), uv);
    if (index == 2) return texture(sampler2D(Cookie2, CookieSampler2), uv);
    if (index == 3) return texture(sampler2D(Cookie3, CookieSampler3), uv);
    if (index == 4) return texture(sampler2D(Cookie4, CookieSampler4), uv);
    return texture(sampler2D(Cookie5, CookieSampler5), uv);
}
