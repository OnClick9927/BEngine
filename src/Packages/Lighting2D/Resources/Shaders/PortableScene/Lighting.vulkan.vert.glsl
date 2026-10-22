#version 450
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec4 aColor;
layout(location = 2) in vec2 aTexCoord;
layout(set = 0, binding = 0) uniform SceneLighting
{
    float uViewportWidth;
    float uViewportHeight;
    vec2 padding;
    vec4 uLighting[__LIGHTING_CAPACITY__];
};
layout(location = 0) out vec4 vColor;
layout(location = 1) out vec2 vTexCoord;
layout(location = 2) out vec2 vPosition;
void main()
{
    vec2 normalized = aPosition / vec2(uViewportWidth, uViewportHeight);
    gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
    vColor = aColor;
    vTexCoord = aTexCoord;
    vPosition = aPosition;
}
