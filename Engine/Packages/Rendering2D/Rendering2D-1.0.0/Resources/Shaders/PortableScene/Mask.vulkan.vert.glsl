#version 450
layout(location=0) in vec2 aPosition;
layout(location=1) in vec4 aColor;
layout(location=2) in vec2 aTexCoord;
layout(set=0,binding=0) uniform SceneViewport { float uViewportWidth; float uViewportHeight; vec4 uMaskSettings; };
layout(location=0) out vec2 vTexCoord;
void main() {
    vec2 uv = aPosition / vec2(uViewportWidth, uViewportHeight);
    gl_Position = vec4(uv.x*2.0-1.0, 1.0-uv.y*2.0, 0.0, 1.0);
    vTexCoord = aTexCoord;
}
