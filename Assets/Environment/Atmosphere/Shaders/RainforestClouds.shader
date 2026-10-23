Shader "Jurassic World/Rainforest Cloud Veil"
{
    Properties
    {
        _Opacity ("Opacity", Range(0,1)) = 0.12
        _Velocity ("Wind", Vector) = (0.0006,0.0002,0,0)
        _Detail ("High quality detail", Float) = 0
        _Color ("Cloud tint", Color) = (0.78,0.82,0.86,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Front ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float _Opacity, _Detail;
            float4 _Velocity, _Color;
            struct varyings { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            varyings vert(appdata_base v)
            {
                varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                // Render at sky depth regardless of the sphere's physical radius.
                #if defined(UNITY_REVERSED_Z)
                    o.position.z = 0.00001 * o.position.w;
                #else
                    o.position.z = 0.99999 * o.position.w;
                #endif
                o.direction = mul(unity_ObjectToWorld, v.vertex).xyz - _WorldSpaceCameraPos;
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),
                    lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float4 frag(varyings i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float horizon = smoothstep(0.12,0.45,d.y);
                float2 p = d.xz / max(0.25,d.y) * 3 + _Time.y * _Velocity.xy;
                float n = noise(p)*0.6 + noise(p*2.07+13)*0.3 + noise(p*4.1+31)*0.1;
                if (_Detail > 0.5) n = n*0.95 + noise(p*8.3)*0.05;
                return float4(_Color.rgb, smoothstep(0.48,0.78,n)*horizon*_Opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
