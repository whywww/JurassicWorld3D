Shader "Jurassic World/Vegetation Prototype Cutout"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _AlphaTex ("Separate Opacity", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff
        #pragma multi_compile_instancing
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _AlphaTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _Smoothness;
        half _BumpScale;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_AlphaTex;
            float2 uv_BumpMap;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = albedo.rgb;
            o.Alpha = albedo.a * tex2D(_AlphaTex, IN.uv_AlphaTex).r;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_BumpMap), _BumpScale);
            o.Metallic = 0;
            o.Smoothness = _Smoothness;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}
