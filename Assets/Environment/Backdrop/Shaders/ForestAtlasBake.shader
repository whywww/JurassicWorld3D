Shader "Hidden/Jurassic World/Forest Atlas Bake"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Cutout", Float) = 0.3
        _LeafBillboards ("Expand Tree Creator leaves", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off ZWrite On
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "TerrainEngine.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color;
            float _Cutoff, _LeafBillboards;
            struct output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; };
            output vert(appdata_full v)
            {
                output o;
                if (_LeafBillboards > 0.5) ExpandBillboard(UNITY_MATRIX_IT_MV,v.vertex,v.normal,v.tangent);
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord,_MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(output i):SV_Target
            {
                float4 sample = tex2D(_MainTex,i.uv)*_Color;
                clip(sample.a-_Cutoff-0.0001);
                float sunlight = saturate(dot(normalize(i.normal),normalize(_WorldSpaceLightPos0.xyz)));
                return float4(sample.rgb*lerp(0.48,0.92,sunlight),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
