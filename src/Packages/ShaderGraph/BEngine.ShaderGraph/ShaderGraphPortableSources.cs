using System.Text;
using System.Text.RegularExpressions;

namespace BEngine.ShaderGraph;

internal static class ShaderGraphPortableSources
{
    private static string Types(string code, bool wgsl)
    {
        for (var n = 2; n <= 4; n++) code = Regex.Replace(code, $@"\bvec{n}\b", wgsl ? $"vec{n}<f32>" : $"float{n}");
        return code;
    }
    internal static (string Vertex, string Fragment) Hlsl(int capacity, int lights, int textures, string lighting, string functions, string body)
    {
        var common = new StringBuilder($"cbuffer GraphParameters : register(b0) {{ float uViewportWidth; float uViewportHeight; float uTime; float padding; float4 uParameters[{capacity}];");
        if (lights > 0) common.Append($"float4 uLighting[{lights}];");
        common.AppendLine("};\nstruct Varyings { float4 position : SV_POSITION; float4 color : COLOR0; float2 uv : TEXCOORD0; float2 point : TEXCOORD1; };");
        var vertex = common + "Varyings main(float2 aPosition : POSITION, float4 aColor : COLOR0, float2 aTexCoord : TEXCOORD0) { Varyings result; float2 p = aPosition / float2(uViewportWidth, uViewportHeight); result.position = float4(p.x * 2.0 - 1.0, 1.0 - p.y * 2.0, 0.0, 1.0); result.color = aColor; result.uv = aTexCoord; result.point = aPosition; return result; }";
        var fragment = new StringBuilder(common.ToString());
        for (var i = 0; i < textures; i++) fragment.AppendLine($"Texture2D<float4> GraphTexture{i} : register(t{i}); SamplerState GraphSampler{i} : register(s{i});");
        string Translate(string code)
        {
            code = Types(code, false);
            code = Regex.Replace(code, @"\bfloat([234])\(([-+\d.eE]+|uTime|n\d+\.[rgbaxyzw])\)", match =>
                $"float{match.Groups[1].Value}(" + string.Join(", ", Enumerable.Repeat(match.Groups[2].Value, int.Parse(match.Groups[1].Value))) + ")");
            code = Regex.Replace(code, @"float4\((float4\([^()]*\)\.[rgba])\)", match => "float4(" + string.Join(", ", Enumerable.Repeat(match.Groups[1].Value, 4)) + ")");
            code = Regex.Replace(code, @"\bmix\b", "lerp"); code = Regex.Replace(code, @"\bfract\b", "frac");
            code = Regex.Replace(code, @"lessThan\((uv), (float2\([^()]*\))\)", "($1 < $2)");
            code = Regex.Replace(code, @"greaterThan\((uv), (float2\([^()]*\))\)", "($1 > $2)");
            return Regex.Replace(code, @"const float2 samples\[4\] = float2\[4\]\((.*?)\);", "static const float2 samples[4] = { $1 };", RegexOptions.Singleline);
        }
        fragment.AppendLine(Translate(lighting + functions));
        fragment.AppendLine("float4 main(Varyings input) : SV_TARGET { float4 vColor = input.color; float2 vTexCoord = input.uv; float2 vPosition = input.point; float4 fragColor;");
        fragment.AppendLine(Translate(body)); fragment.AppendLine("return fragColor; }");
        return (Regex.Replace(vertex, @"\bpoint\b", "samplePoint"), Regex.Replace(fragment.ToString(), @"\bpoint\b", "samplePoint"));
    }
    internal static (string Vertex, string Fragment) Wgsl(int capacity, int lights, int textures, int cookieFirst, int cookieCount, string functions, string body)
    {
        var common = new StringBuilder($"struct GraphParameters {{ uViewportWidth: f32, uViewportHeight: f32, uTime: f32, padding: f32, uParameters: array<vec4<f32>, {capacity}>,");
        if (lights > 0) common.Append($"uLighting: array<vec4<f32>, {lights}>,");
        common.AppendLine("}\n@group(0) @binding(0) var<uniform> graph: GraphParameters;\nstruct Varyings { @builtin(position) position: vec4<f32>, @location(0) color: vec4<f32>, @location(1) uv: vec2<f32>, @location(2) point: vec2<f32>, }");
        var vertex = common + "@vertex fn main(@location(0) aPosition: vec2<f32>, @location(1) aColor: vec4<f32>, @location(2) aTexCoord: vec2<f32>) -> Varyings { var result: Varyings; let p = aPosition / vec2<f32>(graph.uViewportWidth, graph.uViewportHeight); result.position = vec4<f32>(p.x * 2.0 - 1.0, 1.0 - p.y * 2.0, 0.0, 1.0); result.color = aColor; result.uv = aTexCoord; result.point = aPosition; return result; }";
        var fragment = new StringBuilder(common.ToString());
        for (var i = 0; i < textures; i++) fragment.AppendLine($"@group(0) @binding({1 + i * 2}) var GraphTexture{i}: texture_2d<f32>; @group(0) @binding({2 + i * 2}) var GraphSampler{i}: sampler;");
        fragment.AppendLine("var<private> vColor: vec4<f32>; var<private> vTexCoord: vec2<f32>; var<private> vPosition: vec2<f32>;");
        if (lights > 0)
        {
            fragment.AppendLine("fn sampleCookie(index: i32, uv: vec2<f32>) -> vec4<f32> {");
            for (var i = 0; i < cookieCount; i++) fragment.AppendLine($"if (index == {i}) {{ return textureSampleLevel(GraphTexture{cookieFirst + i}, GraphSampler{cookieFirst + i}, uv, 0.0); }}");
            fragment.AppendLine("return vec4<f32>(1.0); }\n" + WgslLighting);
        }
        fragment.AppendLine(functions);
        body = Regex.Replace(body, @"vec4 (n\d+) =", "var $1 =");
        // WGSL disallows assignment to a multi-component swizzle.
        body = Regex.Replace(body, @"fragColor\.rgb = clamp\((.*), 0\.0, 1\.0\);", "fragColor = vec4(clamp($1, vec3(0.0), vec3(1.0)), fragColor.a);");
        body = Types(body, true);
        body = Regex.Replace(body, @"\b(uParameters|uTime)\b", "graph.$1");
        fragment.AppendLine("@fragment fn main(input: Varyings) -> @location(0) vec4<f32> { vColor = input.color; vTexCoord = input.uv; vPosition = input.point; var fragColor: vec4<f32>;");
        fragment.AppendLine(body); fragment.AppendLine("return fragColor; }");
        return (vertex, fragment.ToString());
    }
    private const string WgslLighting = """
        fn cross2(a: vec2<f32>, b: vec2<f32>) -> f32 { return a.x * b.y - a.y * b.x; }
        fn occluded(origin: vec2<f32>, point: vec2<f32>, firstEdge: i32, edgeCount: i32) -> bool {
            let ray = point - origin;
            for (var index = 0; index < edgeCount; index++) {
                let endpoints = graph.uLighting[firstEdge + index]; let edge = endpoints.zw - endpoints.xy;
                let denominator = cross2(ray, edge); if (abs(denominator) <= 0.00000001) { continue; }
                let offset = endpoints.xy - origin; let rayTime = cross2(offset, edge) / denominator; let edgeTime = cross2(offset, ray) / denominator;
                if (rayTime > 0.000001 && rayTime < 0.999999 && edgeTime >= 0.0 && edgeTime <= 1.0) { return true; }
            }
            return false;
        }
        fn shapeAt(point: vec2<f32>, firstEdge: i32, edgeCount: i32, falloff: f32) -> f32 {
            var inside = false; var nearest = 1e20;
            for (var index = 0; index < edgeCount; index++) {
                let edge = graph.uLighting[firstEdge + index]; let a = edge.xy; let b = edge.zw; let delta = b - a;
                let squared = dot(delta, delta); if (squared <= 1e-12) { continue; }
                nearest = min(nearest, length(point - (a + delta * clamp(dot(point - a, delta) / squared, 0.0, 1.0))));
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) { inside = !inside; }
            }
            if (inside || nearest <= 0.00001) { return 1.0; }
            var value = 0.0; if (falloff > 0.0) { value = 1.0 - clamp(nearest / falloff, 0.0, 1.0); } return value * value;
        }
        fn lightAt(point: vec2<f32>, normal: vec3<f32>, start: i32) -> vec3<f32> {
            let position = graph.uLighting[start]; let cone = graph.uLighting[start + 2]; let shadow = graph.uLighting[start + 3]; let parameters = graph.uLighting[start + 4];
            let delta = point - position.xy; let distance = length(delta); var radial = 1.0; var cookie = vec3<f32>(1.0);
            if (shadow.x == 3.0) { radial = shapeAt(point, start + 8 + i32(shadow.z), i32(shadow.w), parameters.y); }
            else if (shadow.x == 4.0) {
                if (parameters.z < 0.0) { return vec3<f32>(0.0); }
                let uv = vec2<f32>(dot(graph.uLighting[start + 5].xyz, vec3<f32>(point, 1.0)), dot(graph.uLighting[start + 6].xyz, vec3<f32>(point, 1.0)));
                if (any(uv < vec2<f32>(0.0)) || any(uv > vec2<f32>(1.0))) { return vec3<f32>(0.0); }
                let rect = graph.uLighting[start + 7]; let texel = sampleCookie(i32(parameters.z), rect.xy + uv * rect.zw); cookie = texel.rgb * texel.a;
            } else {
                if (distance >= position.z) { return vec3<f32>(0.0); }
                if (distance > position.w) { radial = clamp((position.z - distance) / max(0.000001, position.z - position.w), 0.0, 1.0); }
                radial *= radial;
            }
            var angular = 1.0;
            if (shadow.x == 2.0 && distance > 0.000001) {
                let alignment = dot(cone.xy, delta / distance); if (alignment <= cone.z) { return vec3<f32>(0.0); }
                if (cone.w > cone.z) { angular = clamp((alignment - cone.z) / (cone.w - cone.z), 0.0, 1.0); }
            }
            var visibility = 1.0; if (shadow.z > 0.0 && occluded(position.xy, point, start + 8, i32(shadow.z))) { visibility = 1.0 - shadow.y; }
            var diffuse = 1.0; if (graph.uLighting[5].y > 0.5) { diffuse = max(0.0, dot(normal, normalize(vec3<f32>(position.xy - point, parameters.x)))); }
            return cookie * (radial * angular * angular * visibility * diffuse);
        }
        fn lightingNormal(encoded: vec3<f32>) -> vec3<f32> {
            let tangent = vec3<f32>((encoded.xy * 2.0 - vec2<f32>(1.0)) * graph.uLighting[5].x, encoded.z * 2.0 - 1.0);
            let transformed = vec3<f32>(graph.uLighting[4].xy * tangent.x + graph.uLighting[4].zw * tangent.y, tangent.z);
            if (dot(transformed, transformed) > 1e-10) { return normalize(transformed); } return vec3<f32>(0.0, 0.0, 1.0);
        }
        fn evaluateLighting(position: vec2<f32>, normal: vec3<f32>) -> vec3<f32> {
            let axisX = graph.uLighting[1].zw; let axisY = graph.uLighting[2].xy; let world = graph.uLighting[1].xy + position.x * axisX + position.y * axisY;
            var lighting = graph.uLighting[0].rgb; var start = 6;
            let samples = array<vec2<f32>,4>(vec2<f32>(-0.125,-0.375),vec2<f32>(0.375,-0.125),vec2<f32>(0.125,0.375),vec2<f32>(-0.375,0.125));
            for (var light = 0; light < i32(graph.uLighting[0].w); light++) {
                var contribution = vec3<f32>(0.0);
                for (var sampleIndex = 0; sampleIndex < 4; sampleIndex++) { contribution += lightAt(world + axisX * samples[sampleIndex].x + axisY * samples[sampleIndex].y, normal, start); }
                let color = graph.uLighting[start + 1]; lighting += color.rgb * (color.a * contribution * 0.25);
                start += 8 + i32(graph.uLighting[start + 3].z) + i32(graph.uLighting[start + 3].w);
            }
            return lighting;
        }
        """;
}
