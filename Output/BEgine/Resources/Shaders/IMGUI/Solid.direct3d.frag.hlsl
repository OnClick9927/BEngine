struct I { float4 Position : SV_Position; float4 Color : COLOR0; };
float4 main(I i) : SV_Target0 { return i.Color; }
