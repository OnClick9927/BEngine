#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec4 aColor;
uniform float uViewportWidth;
uniform float uViewportHeight;
out vec4 vColor;
void main()
{
    vec2 normalized = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);
    gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
    vColor = aColor;
}
