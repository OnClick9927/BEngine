#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec4 aColor;
layout(location = 2) in vec2 aTexCoord;
uniform float uViewportWidth;
uniform float uViewportHeight;
out vec4 vColor;
out vec2 vTexCoord;
void main() {
    vec2 p = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);
    gl_Position = vec4(p.x * 2.0 - 1.0, 1.0 - p.y * 2.0, 0.0, 1.0);
    vColor = aColor;
    vTexCoord = aTexCoord;
}
