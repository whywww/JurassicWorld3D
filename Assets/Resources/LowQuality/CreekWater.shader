Shader "Jurassic World/Low Creek Water"
{
    Properties
    {
        _Color ("Water color", Color) = (.04,.13,.105,1)
        _BumpMap ("Flow normal", 2D) = "bump" {}
        _Reflection ("Environment", Cube) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _BumpMap;
            samplerCUBE _Reflection;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            fixed4 _Color;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; UNITY_FOG_COORDS(2) float4 screen:TEXCOORD3; float eye:TEXCOORD4; };
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screen = ComputeScreenPos(o.pos); o.eye = -UnityObjectToViewPos(v.vertex).z;
                UNITY_TRANSFER_FOG(o,o.pos); return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                half3 normal = UnpackNormal(tex2D(_BumpMap, i.uv + float2(.018,-.045) * _Time.y));
                normal = normalize(half3(normal.x * .22, 1, normal.y * .22));
                half3 view = normalize(_WorldSpaceCameraPos - i.world);
                half fresnel = .08 + .5 * pow(1 - saturate(dot(view,normal)), 4);
                fixed3 sky = texCUBE(_Reflection, reflect(-view,normal)).rgb;
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                half shore = saturate((depth - i.eye - .01) * 3);
                shore *= smoothstep(0,.12,min(i.uv.x,1-i.uv.x));
                fixed4 color = fixed4(lerp(_Color.rgb,sky,fresnel),shore * .85);
                UNITY_APPLY_FOG(i.fogCoord,color); return color;
            }
            ENDCG
        }
    }
}
