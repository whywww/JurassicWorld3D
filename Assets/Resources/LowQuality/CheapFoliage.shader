Shader "Jurassic World/Low Cheap Foliage"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _AlphaTex ("Opacity", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = .35
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Lambert addshadow alphatest:_Cutoff noforwardadd
        #pragma multi_compile_instancing
        #pragma target 3.0
        sampler2D _MainTex, _AlphaTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; float2 uv_AlphaTex; };
        void surf(Input IN,inout SurfaceOutput o)
        {
            fixed4 color = tex2D(_MainTex,IN.uv_MainTex) * _Color;
            o.Albedo = color.rgb;
            o.Alpha = color.a * tex2D(_AlphaTex,IN.uv_AlphaTex).r;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}
