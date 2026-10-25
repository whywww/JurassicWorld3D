Shader "Jurassic World/Interactive Tree Physical Bark"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpSpecMap ("Packed Normal Specular", 2D) = "bump" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = .3
        _ShadowStrength ("Shadow Strength", Range(0,1)) = .8
        _OcclusionStrength ("Baked Occlusion Strength", Range(0,1)) = 1
        _AlbedoGain ("Albedo Gain", Range(1,4)) = 1
        _InstanceTintStrength ("Instance Tint Strength", Range(0,1)) = 1
        _NormalStrength ("Normal Strength", Range(0,1)) = 1
        _Smoothness ("Bark Smoothness", Range(0,1)) = .18
        _DirectLightScale ("Direct Light Scale", Range(0,1)) = 1
        _ShadowLift ("Shadow Lift", Range(0,0.2)) = 0
        [HideInInspector] _TreeInstanceColor ("Tree Color", Vector) = (1,1,1,1)
        [HideInInspector] _TreeInstanceScale ("Tree Scale", Vector) = (1,1,1,1)
        [HideInInspector] _SquashAmount ("Squash", Float) = 1
        [HideInInspector] _FoliageActive ("Active", Float) = 0
        [HideInInspector] _LeafDisplacement ("Leaf Displacement", Float) = .45
        [HideInInspector] _BranchDisplacement ("Branch Displacement", Float) = .2
    }
    SubShader
    {
        Tags { "RenderType"="TreeBark" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Bark fullforwardshadows addshadow vertex:vert nolightmap exclude_path:deferred
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityBuiltin3xTreeLibrary.cginc"
        #include "FoliageInteraction.cginc"
        #include "UnityPBSLighting.cginc"
        sampler2D _MainTex, _BumpSpecMap;
        half _OcclusionStrength, _AlbedoGain, _InstanceTintStrength, _NormalStrength, _Smoothness;
        half _DirectLightScale, _ShadowLift;

        half4 LightingBark(SurfaceOutputStandard s, float3 viewDir, UnityGI gi)
        {
            gi.light.color *= _DirectLightScale;
            return LightingStandard(s, viewDir, gi);
        }

        void LightingBark_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(s, data, gi);
        }

        struct Input { float2 uv_MainTex; fixed4 color : COLOR; float3 worldNormal; INTERNAL_DATA };
        void vert(inout appdata_full v)
        {
            TreeVertBark(v);
            v.color.rgb = _Color.rgb * lerp(half3(1,1,1), _TreeInstanceColor.rgb, _InstanceTintStrength);
            ApplyFoliageInteraction(v);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex);
            half3 albedoLinear = saturate(albedo.rgb * IN.color.rgb * _AlbedoGain) * lerp(1, IN.color.a, _OcclusionStrength);
            o.Albedo = albedoLinear;
            o.Alpha = albedo.a;
            o.Normal = normalize(lerp(half3(0,0,1), UnpackNormalDXT5nm(tex2D(_BumpSpecMap, IN.uv_MainTex)), _NormalStrength));
            // Scene has a single directional light, so base-pass emission cannot double count in forward-add.
            half3 worldNormal = WorldNormalVector(IN, o.Normal);
            half backSide = saturate(1.0 - dot(worldNormal, normalize(_WorldSpaceLightPos0.xyz)));
            o.Emission = albedoLinear * _ShadowLift * backSide;
            o.Metallic = 0;
            o.Smoothness = _Smoothness;
        }
        ENDCG
    }
    Fallback "Diffuse"
    Dependency "BillboardShader" = "Hidden/Nature/Tree Creator Bark Rendertex"
}
