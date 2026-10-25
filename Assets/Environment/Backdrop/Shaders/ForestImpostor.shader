Shader "Jurassic World/Forest Impostor"
{
    Properties
    {
        _MainTex ("16 baked directions", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.38
        _HazeColor ("Distant air", Color) = (0.4,0.5,0.5,1)
        _HazeNear ("Haze start", Float) = 55
        _HazeFar ("Haze end", Float) = 230
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "DisableBatching"="True" }
        Cull Off ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _HazeColor;
            float _Cutoff, _HazeNear, _HazeFar;
            struct input { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float3 view:TEXCOORD1; float3 world:TEXCOORD2; };
            output vert(input v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                output o;
                float3 center = mul(unity_ObjectToWorld,float4(0,0,0,1)).xyz;
                float3 view = _WorldSpaceCameraPos-center;
                float3 right = normalize(cross(float3(0,1,0),float3(view.x,0,view.z)));
                float scaleX = length(unity_ObjectToWorld._m00_m10_m20);
                float scaleY = length(unity_ObjectToWorld._m01_m11_m21);
                o.world = center + right*v.vertex.x*scaleX + float3(0,v.vertex.y*scaleY,0);
                o.position = UnityWorldToClipPos(o.world);
                float3 localView = mul((float3x3)unity_WorldToObject,view);
                float frame = frac(atan2(localView.x,localView.z)/UNITY_TWO_PI+1)*16;
                o.view = float3(floor(frame),fmod(floor(frame)+1,16),frac(frame));
                o.uv = v.uv;
                return o;
            }
            float2 atlasUV(float2 uv,float frame)
            {
                // Padding stays within each 512px tile, including its mip levels.
                return (float2(fmod(frame,4),floor(frame/4))+clamp(uv,0.004,0.996))*0.25;
            }
            float4 frag(output i):SV_Target
            {
                float4 a = tex2D(_MainTex,atlasUV(i.uv,i.view.x));
                float4 b = tex2D(_MainTex,atlasUV(i.uv,i.view.y));
                // Interpolate coverage as well as color to avoid discrete direction popping.
                float weight = smoothstep(0,1,i.view.z);
                float4 sample = lerp(a,b,weight);
                sample.rgb = lerp(a.rgb*a.a,b.rgb*b.a,weight)/max(sample.a,0.0001);
                clip(sample.a-_Cutoff);
                float haze = lerp(0.08,0.9,smoothstep(_HazeNear,_HazeFar,length(i.world-_WorldSpaceCameraPos)));
                return float4(lerp(sample.rgb,_HazeColor.rgb,haze),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
