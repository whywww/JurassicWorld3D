Shader "Jurassic World/Rainforest Soft Sun Shaft"
{
    Properties
    {
        _MainTex ("Soft shaft mask", 2D) = "white" {}
        _Tint ("Sunlight", Color) = (1,0.96,0.84,0.018)
        _DepthFade ("Intersection fade metres", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-4" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _Tint;
            float _DepthFade;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct varyings
            {
                float4 position:SV_POSITION;
                float2 uv:TEXCOORD0;
                float4 screen:TEXCOORD1;
                float3 world:TEXCOORD2;
                float3 normal:TEXCOORD3;
                float eyeDepth:TEXCOORD4;
            };
            varyings vert(appdata v)
            {
                varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.position);
                o.eyeDepth = -UnityObjectToViewPos(v.vertex).z;
                o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(varyings i):SV_Target
            {
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float intersection = saturate((depth-i.eyeDepth)/max(_DepthFade,0.01));
                float3 view = normalize(_WorldSpaceCameraPos-i.world);
                float3 axis = normalize(mul((float3x3)unity_ObjectToWorld,float3(0,1,0)));
                float angle = 1-pow(abs(dot(view,axis)),4);
                float edge = pow(saturate(abs(dot(normalize(i.normal),view))),2.5);
                float nearFade = smoothstep(0.6,2.5,i.eyeDepth);
                float farFade = 1-smoothstep(40,60,i.eyeDepth);
                float alpha = tex2D(_MainTex,i.uv).a*_Tint.a*intersection*angle*edge*nearFade*farFade;
                return float4(_Tint.rgb,alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
