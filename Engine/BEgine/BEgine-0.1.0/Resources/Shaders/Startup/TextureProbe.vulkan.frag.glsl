#version 450
layout(location = 0) in vec2 texCoord;
layout(set = 0, binding = 1) uniform texture2D UITexture;
layout(set = 0, binding = 2) uniform sampler UISampler;
layout(location = 0) out vec4 color;
void main() { color = texture(sampler2D(UITexture, UISampler), texCoord); }
