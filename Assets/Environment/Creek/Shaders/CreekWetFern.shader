Shader "Jurassic World/Creek Wet Fern"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo and opacity", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.35
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _MetallicGlossMap ("Original smoothness in alpha", 2D) = "black" {}
        _Glossiness ("Original smoothness scale", Range(0,1)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _WetMask ("Creek proximity mask", 2D) = "black" {}
        _WetRegion ("World XZ bounds", Vector) = (-100,-100,200,200)
        _WetStrength ("Wetness", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "CreekWetness.cginc"
        sampler2D _MainTex, _BumpMap, _MetallicGlossMap;
        half4 _Color;
        half _BumpScale, _Glossiness;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 color = tex2D(_MainTex,IN.uv_MainTex)*_Color;
            half wet = CreekWetness(IN.worldPos);
            half smoothness = tex2D(_MetallicGlossMap,IN.uv_MainTex).a*_Glossiness;
            o.Albedo = color.rgb*lerp(1,0.96,wet);
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap,IN.uv_BumpMap),_BumpScale);
            o.Metallic = 0;
            o.Smoothness = lerp(smoothness,max(smoothness,min(0.72,smoothness+0.10)),wet);
            o.Alpha = color.a;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}
