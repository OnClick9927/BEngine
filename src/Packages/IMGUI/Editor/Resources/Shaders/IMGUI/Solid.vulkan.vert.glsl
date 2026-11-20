#version 450
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec4 aColor;
layout(set = 0, binding = 0) uniform Viewport { float uViewportWidth; float uViewportHeight; };
layout(location = 0) out vec4 vColor;
void main() {
    vec2 p = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);
    gl_Position = vec4(p.x * 2.0 - 1.0, 1.0 - p.y * 2.0, 0.0, 1.0);
    vColor = aColor;
}
