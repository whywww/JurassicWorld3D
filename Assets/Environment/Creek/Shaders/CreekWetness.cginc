#ifndef CREEK_LOCAL_WETNESS_INCLUDED
#define CREEK_LOCAL_WETNESS_INCLUDED
sampler2D _WetMask;
float4 _WetRegion;
half _WetStrength;
half CreekWetness(float3 world)
{
    float2 uv = (world.xz-_WetRegion.xy)/_WetRegion.zw;
    float inside = step(0,uv.x)*step(0,uv.y)*step(uv.x,1)*step(uv.y,1);
    half patch = 0.82+0.18*sin(world.x*2.1+sin(world.z*1.7));
    half height = lerp(1,0.65,smoothstep(0.15,1.5,world.y));
    return saturate(tex2D(_WetMask,uv).r*_WetStrength*inside*patch*height);
}
#endif
