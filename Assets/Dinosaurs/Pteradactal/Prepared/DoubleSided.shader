Shader "JurassicWorld/Pteradactal Double Sided"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _MetallicGlossMap ("Metallic (R), Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0.05
        _Glossiness ("Smoothness", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _OcclusionMap, _MetallicGlossMap;
        fixed4 _Color;
        half _Metallic, _Glossiness;
        struct Input { float2 uv_MainTex; float facing : VFACE; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Normal.z *= IN.facing >= 0 ? 1 : -1;
            o.Occlusion = tex2D(_OcclusionMap, IN.uv_MainTex).r;
            fixed4 packed = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = packed.r * _Metallic;
            o.Smoothness = packed.a * _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
