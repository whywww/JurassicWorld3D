#ifndef JURASSIC_FOLIAGE_INTERACTION_INCLUDED
#define JURASSIC_FOLIAGE_INTERACTION_INCLUDED
#include "UnityCG.cginc"
sampler3D _FoliageField;
float4x4 _FoliageWorldToRoot;
float3 _FoliageFieldMin, _FoliageFieldSize, _FoliageFieldDimensions;
float _FoliageActive, _LeafDisplacement, _BranchDisplacement;

// UV4 stores the baked root/attachment mask and whether this vertex belongs to wood.
void ApplyFoliageInteraction(inout appdata_full v)
{
    if (_FoliageActive < .5 || v.texcoord3.x <= 0) return;
    float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
    float3 root = mul(_FoliageWorldToRoot, float4(world, 1)).xyz;
    float3 coordinate = saturate((root - _FoliageFieldMin) / _FoliageFieldSize);
    // CPU nodes cover the two end planes; address their texel centres, not texture edges.
    coordinate = (coordinate * (_FoliageFieldDimensions - 1) + .5) / _FoliageFieldDimensions;
    float3 offset = tex3Dlod(_FoliageField, float4(coordinate, 0)).xyz;
    float ratio = lerp(1, _BranchDisplacement / max(.001, _LeafDisplacement), saturate(v.texcoord3.y));
    offset *= saturate(v.texcoord3.x) * ratio;
    v.vertex.xyz += mul((float3x3)unity_WorldToObject, offset);
}
#endif
