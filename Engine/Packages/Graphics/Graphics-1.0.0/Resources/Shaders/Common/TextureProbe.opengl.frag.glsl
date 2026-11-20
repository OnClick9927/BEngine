#version 330 core
in vec2 texCoord;
uniform sampler2D uTexture;
out vec4 color;
void main() { color = texture(uTexture, texCoord); }
