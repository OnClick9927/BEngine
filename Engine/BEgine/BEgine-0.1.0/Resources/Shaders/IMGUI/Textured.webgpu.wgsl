struct Viewport { width: f32, height: f32 }; @group(0) @binding(0) var<uniform> viewport: Viewport;
@group(0) @binding(1) var canvasTexture: texture_2d<f32>;
@group(0) @binding(2) var canvasSampler: sampler;
struct I { @location(0) position: vec2<f32>, @location(1) color: vec4<f32>, @location(2) uv: vec2<f32> };
struct O { @builtin(position) position: vec4<f32>, @location(0) color: vec4<f32>, @location(1) uv: vec2<f32> };
@vertex fn vs_main(i:I)->O { var o:O; let p=i.position/vec2<f32>(viewport.width,viewport.height);
  o.position=vec4<f32>(p.x*2.0-1.0,1.0-p.y*2.0,0.0,1.0); o.color=i.color; o.uv=i.uv; return o; }
@fragment fn fs_main(i:O)->@location(0) vec4<f32> {
  return textureSample(canvasTexture, canvasSampler, i.uv) * i.color; }
