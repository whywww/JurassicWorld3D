Shader "Jurassic World/Backdrop Haze"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0
        _HazeColor ("Distant air", Color) = (0.64,0.73,0.76,1)
        _HazeNear ("Haze start", Float) = 65
        _HazeFar ("Haze end", Float) = 500
        _HazeMin ("Minimum haze", Range(0,1)) = 0.15
        _HazeMax ("Maximum haze", Range(0,1)) = 0.85
        _Saturation ("Saturation", Range(0,1)) = 0.45
        _VertexTint ("Baked ground color variation", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Off ZWrite On
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color, _HazeColor;
            float _Cutoff, _HazeNear, _HazeFar, _HazeMin, _HazeMax, _Saturation, _VertexTint;
            struct varyings
            {
                float4 position:SV_POSITION;
                float2 uv:TEXCOORD0;
                float3 world:TEXCOORD1;
                float3 normal:TEXCOORD2;
                float3 tint:COLOR0;
            };
            varyings vert(appdata_full v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord,_MainTex);
                o.world = mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.tint = lerp(float3(1,1,1),v.color.rgb,_VertexTint);
                return o;
            }
            float4 frag(varyings i):SV_Target
            {
                float4 sample = tex2D(_MainTex,i.uv) * _Color;
                sample.rgb *= i.tint;
                clip(sample.a-_Cutoff-0.0001);
                float3 c = lerp(dot(sample.rgb,float3(0.2126,0.7152,0.0722)).xxx,sample.rgb,_Saturation);
                float light = saturate(dot(normalize(i.normal),normalize(_WorldSpaceLightPos0.xyz)));
                c *= lerp(0.65,1.0,light);
                float distance = length(i.world-_WorldSpaceCameraPos);
                float haze = lerp(_HazeMin,_HazeMax,smoothstep(_HazeNear,_HazeFar,distance));
                return float4(lerp(c,_HazeColor.rgb,haze),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
