#include "FoliageInteraction.cginc"
sampler2D _MainTex, _AlphaTex, _BumpMap, _MetallicGlossMap, _EmissionMap;
fixed4 _Color, _EmissionColor, _TreeInstanceColor;
half _Glossiness, _Metallic, _BumpScale, _HasMetallicGlossMap;
struct Input { float2 uv_MainTex; float2 uv_AlphaTex; float2 uv_BumpMap; float2 uv_EmissionMap; };
void vert(inout appdata_full v) { ApplyFoliageInteraction(v); }
void surf(Input IN, inout SurfaceOutputStandard o)
{
    fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color * _TreeInstanceColor;
    o.Albedo = albedo.rgb;
    o.Alpha = albedo.a * tex2D(_AlphaTex, IN.uv_AlphaTex).r;
    fixed4 metallic = tex2D(_MetallicGlossMap, IN.uv_MainTex);
    o.Metallic = lerp(_Metallic, metallic.r * _Metallic, _HasMetallicGlossMap);
    o.Smoothness = lerp(_Glossiness, metallic.a * _Glossiness, _HasMetallicGlossMap);
    o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_BumpMap), _BumpScale);
    o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor.rgb;
}
