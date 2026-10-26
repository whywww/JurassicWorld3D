Shader "Hidden/JurassicWorld/DinosaurVisibilityMask"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        _UseSceneDepth ("Compare main camera depth", Float) = 0
    }
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
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _CameraDepthTexture_TexelSize;
            float _UseSceneDepth;
            struct v2f { float4 position : SV_POSITION; float4 screen : TEXCOORD0; float eyeDepth : TEXCOORD1; };
            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.screen = ComputeScreenPos(output.position);
                output.eyeDepth = -UnityObjectToViewPos(input.vertex).z;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                if (_UseSceneDepth > .5)
                {
                    // The low-resolution mask and native depth have different pixel centres.
                    // Use the conservative depth of the four surrounding texels to avoid
                    // self-occluding thin, slanted wings at a native-depth pixel boundary.
                    float2 uv = input.screen.xy / input.screen.w;
                    float2 halfTexel = abs(_CameraDepthTexture_TexelSize.xy) * .5;
                    float a = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv + float2(-halfTexel.x, -halfTexel.y));
                    float b = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv + float2(halfTexel.x, -halfTexel.y));
                    float c = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv + float2(-halfTexel.x, halfTexel.y));
                    float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv + halfTexel);
                    #if defined(UNITY_REVERSED_Z)
                    float rawDepth = min(min(a, b), min(c, d));
                    #else
                    float rawDepth = max(max(a, b), max(c, d));
                    #endif
                    float depth = LinearEyeDepth(rawDepth);
                    clip(depth + max(.01, input.eyeDepth * .0001) - input.eyeDepth);
                }
                return fixed4(1, 1, 1, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
