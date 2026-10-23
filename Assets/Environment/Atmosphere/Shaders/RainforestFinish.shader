Shader "Hidden/Jurassic World/Rainforest Finish"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _BloomTex;
        float4 _BlurDirection;
        float _Exposure, _BloomStrength;
        float4 prefilter(v2f_img i) : SV_Target
        {
            float3 c = min(tex2D(_MainTex,i.uv).rgb, 8.0);
            float peak = max(c.r,max(c.g,c.b));
            return float4(c * saturate((peak-1.15)/max(peak,0.0001)),1);
        }
        float4 blur(v2f_img i) : SV_Target
        {
            float2 d = _BlurDirection.xy;
            return tex2D(_MainTex,i.uv)*0.227027
                + (tex2D(_MainTex,i.uv+d*1.384615)+tex2D(_MainTex,i.uv-d*1.384615))*0.316216
                + (tex2D(_MainTex,i.uv+d*3.230769)+tex2D(_MainTex,i.uv-d*3.230769))*0.070270;
        }
        float4 finish(v2f_img i) : SV_Target
        {
            float4 source = tex2D(_MainTex,i.uv);
            float3 c = source.rgb;
            #ifdef UNITY_COLORSPACE_GAMMA
                c = GammaToLinearSpace(c);
            #endif
            float3 glow = tex2D(_BloomTex,i.uv).rgb;
            #ifdef UNITY_COLORSPACE_GAMMA
                glow = GammaToLinearSpace(glow);
            #endif
            c = max(0,(c+glow*_BloomStrength)*_Exposure);
            c = saturate((c*(2.51*c+0.03))/(c*(2.43*c+0.59)+0.14));
            #ifdef UNITY_COLORSPACE_GAMMA
                c = LinearToGammaSpace(c);
            #endif
            return float4(c,source.a);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment prefilter
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment blur
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment finish
            ENDCG
        }
    }
    Fallback Off
}
