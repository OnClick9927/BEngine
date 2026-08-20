cbuffer Viewport : register(b0) { float uViewportWidth; float uViewportHeight; };
struct I { float2 Position : POSITION0; float4 Color : COLOR0; };
struct O { float4 Position : SV_Position; float4 Color : COLOR0; };
O main(I i) { O o; float2 p = i.Position / float2(uViewportWidth, uViewportHeight);
  o.Position=float4(p.x*2-1,1-p.y*2,0,1); o.Color=i.Color; return o; }
