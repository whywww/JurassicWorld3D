Shader "Hidden/JurassicWorld/DinosaurVisibilityMask"
{
    Properties { [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4 }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest [_ZTest]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position : SV_POSITION; };
            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }
            fixed4 frag(v2f input) : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
    Fallback Off
}
