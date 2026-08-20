struct FragmentInput { float4 Position : SV_Position; float4 Color : COLOR0; };
float4 main(FragmentInput input) : SV_Target0 { return input.Color; }
