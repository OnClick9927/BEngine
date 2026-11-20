#version 450
layout(location = 0) in vec2 position;
layout(location = 1) in vec2 uv;
layout(set = 0, binding = 0) uniform Viewport { float uViewportWidth; float uViewportHeight; };
layout(location = 0) out vec2 texCoord;
void main() {
    gl_Position = vec4(position.x / uViewportWidth * 2.0 - 1.0,
        1.0 - position.y / uViewportHeight * 2.0, 0.0, 1.0);
    texCoord = uv;
}
