#version 450
layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vTexCoord;
layout(set = 0, binding = 1) uniform texture2D TilemapTexture;
layout(set = 0, binding = 2) uniform sampler TilemapSampler;
layout(location = 0) out vec4 fragColor;
void main() { fragColor = texture(sampler2D(TilemapTexture, TilemapSampler), vTexCoord) * vColor; }
