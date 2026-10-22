#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColorParams;
layout(location = 3) in vec4 aColorFilter;
layout(location = 4) in vec4 aVignette;
layout(location = 5) in vec4 aVignetteColor;
layout(location = 6) in vec4 aBloom;
layout(location = 7) in vec4 aTextureInfo;
uniform float uViewportWidth;
uniform float uViewportHeight;
out vec2 vTexCoord;
flat out vec4 vGradeR;
flat out vec4 vGradeG;
flat out vec4 vVignette;
flat out vec4 vVignetteColor;
flat out vec4 vBloom;
flat out vec4 vTextureInfo;
vec3 hueRotate(vec3 color, float degrees)
{
    float angle = radians(degrees);
    float c = cos(angle);
    float s = sin(angle);
    mat3 matrix = mat3(
        0.299 + 0.701*c + 0.168*s, 0.587 - 0.587*c + 0.330*s, 0.114 - 0.114*c - 0.497*s,
        0.299 - 0.299*c - 0.328*s, 0.587 + 0.413*c + 0.035*s, 0.114 - 0.114*c + 0.292*s,
        0.299 - 0.300*c + 1.250*s, 0.587 - 0.588*c - 1.050*s, 0.114 + 0.886*c - 0.203*s);
    return matrix * color;
}
vec3 gradeColor(vec3 color)
{
    color *= exp2(aColorParams.x);
    float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luminance), color, 1.0 + aColorParams.z);
    color = (color - 0.5) * (1.0 + aColorParams.y) + 0.5;
    if (aColorParams.w != 0.0) color = hueRotate(color, aColorParams.w);
    color *= aColorFilter.rgb;
    return color;
}
flat out vec4 vGradeB;
void main()
{
    vec2 normalized = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);
    gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
    // Render-target rows use OpenGL's bottom-left origin; screen geometry uses
    // the engine's top-left origin. This pass always samples a render target.
    vTexCoord = vec2(aTexCoord.x, 1.0 - aTexCoord.y);
    // Color grading is affine; prepare its rows per vertex rather than
    // evaluating exposure, hue trigonometry and saturation per screen pixel.
    vec3 bias = gradeColor(vec3(0));
    vec3 r = gradeColor(vec3(1,0,0)) - bias;
    vec3 g = gradeColor(vec3(0,1,0)) - bias;
    vec3 b = gradeColor(vec3(0,0,1)) - bias;
    vGradeR = vec4(r.x, g.x, b.x, bias.x);
    vGradeG = vec4(r.y, g.y, b.y, bias.y);
    vGradeB = vec4(r.z, g.z, b.z, bias.z);
    vVignette = aVignette;
    vVignetteColor = aVignetteColor;
    // These parameters are constant across the fullscreen quad. Prepare the
    // bloom weight/UV step and vignette edges once per vertex, not per pixel.
    vBloom = vec4(aBloom.x * 0.125, aBloom.y,
        aTextureInfo.x * aBloom.z, aTextureInfo.y * aBloom.z);
    vTextureInfo = vec4(max(0.0, 1.0 - aVignette.x),
        max(0.001, 1.0 - aVignette.x + aVignette.y), aTextureInfo.z, aTextureInfo.w);
}
