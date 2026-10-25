Shader "Jurassic World/Rainforest Soft Mist"
{
    Properties
    {
        _MainTex ("Soft noise", 2D) = "white" {}
        _DepthFade ("Intersection fade metres", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float _DepthFade;
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct varyings
            {
                float4 position:SV_POSITION;
                float4 color:COLOR;
                float2 uv:TEXCOORD0;
                float4 screen:TEXCOORD1;
                float eyeDepth:TEXCOORD2;
            };
            varyings vert(appdata v)
            {
                varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.position);
                o.eyeDepth = -UnityObjectToViewPos(v.vertex).z;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            float4 frag(varyings i):SV_Target
            {
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                float intersection = saturate((depth-i.eyeDepth)/max(_DepthFade,0.01));
                float nearFade = smoothstep(0.8,4.0,i.eyeDepth);
                float farFade = 1-smoothstep(48,70,i.eyeDepth);
                return float4(i.color.rgb, tex2D(_MainTex,i.uv).a*i.color.a*intersection*nearFade*farFade);
            }
            ENDCG
        }
    }
    Fallback Off
}
