Shader "Jurassic World/Creek Wet Rock"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _MetallicGlossMap ("Original smoothness in alpha", 2D) = "white" {}
        _Glossiness ("Original smoothness", Range(0,1)) = 0.1
        _GlossMapScale ("Original map scale", Range(0,1)) = 1
        _HasSmoothnessMap ("Smoothness map present", Float) = 0
        _WetMask ("Creek proximity mask", 2D) = "black" {}
        _WetRegion ("World XZ bounds", Vector) = (-100,-100,200,200)
        _WetStrength ("Wetness", Range(0,1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "CreekWetness.cginc"
        sampler2D _MainTex, _BumpMap, _MetallicGlossMap;
        half4 _Color;
        half _BumpScale, _Glossiness, _GlossMapScale, _HasSmoothnessMap;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 color = tex2D(_MainTex,IN.uv_MainTex)*_Color;
            half smoothness = lerp(_Glossiness,tex2D(_MetallicGlossMap,IN.uv_MainTex).a*_GlossMapScale,_HasSmoothnessMap);
            half moss = saturate((color.g-max(color.r,color.b))*6);
            half wet = CreekWetness(IN.worldPos)*lerp(1,0.45,moss);
            o.Albedo = color.rgb*lerp(1,0.82,wet);
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap,IN.uv_BumpMap),_BumpScale);
            o.Metallic = 0;
            o.Smoothness = lerp(smoothness,max(smoothness,min(0.65,smoothness+0.28)),wet);
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Standard"
}
