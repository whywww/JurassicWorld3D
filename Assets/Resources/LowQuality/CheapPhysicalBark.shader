Shader "Jurassic World/Low Cheap PhysicalBark"
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
        [HideInInspector] _JWInstanceColor ("Low instance color", Color) = (1,1,1,1)
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
        #pragma surface surf Lambert fullforwardshadows addshadow vertex:vert nolightmap exclude_path:deferred
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityBuiltin3xTreeLibrary.cginc"
        #include "UnityPBSLighting.cginc"
        #ifdef UNITY_INSTANCING_ENABLED
        UNITY_INSTANCING_BUFFER_START(LowTrees)
            UNITY_DEFINE_INSTANCED_PROP(float4, _JWInstanceColor)
        UNITY_INSTANCING_BUFFER_END(LowTrees)
        #else
        fixed4 _JWInstanceColor;
        #endif
        fixed4 LowTreeColor()
        {
            #ifdef UNITY_INSTANCING_ENABLED
            return UNITY_ACCESS_INSTANCED_PROP(LowTrees, _JWInstanceColor);
            #else
            return _JWInstanceColor;
            #endif
        }
        sampler2D _MainTex, _BumpSpecMap;
        half _OcclusionStrength, _AlbedoGain, _InstanceTintStrength, _NormalStrength, _Smoothness;
        half _DirectLightScale, _ShadowLift;

        struct Input { float2 uv_MainTex; fixed4 color : COLOR; float3 worldNormal; INTERNAL_DATA };
        void vert(inout appdata_full v)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            TreeVertBark(v);
            v.color.rgb = _Color.rgb * lerp(half3(1,1,1), LowTreeColor().rgb, _InstanceTintStrength);
        }
        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex);
            half3 albedoLinear = saturate(albedo.rgb * IN.color.rgb * _AlbedoGain) * lerp(1, IN.color.a, _OcclusionStrength);
            o.Albedo = albedoLinear;
            o.Alpha = albedo.a;
            o.Normal = half3(0,0,1);
            // Scene has a single directional light, so base-pass emission cannot double count in forward-add.
            half3 worldNormal = WorldNormalVector(IN, o.Normal);
            half backSide = saturate(1.0 - dot(worldNormal, normalize(_WorldSpaceLightPos0.xyz)));
            o.Emission = albedoLinear * _ShadowLift * backSide;
        }
        ENDCG
    }
    Fallback "Diffuse"
    Dependency "BillboardShader" = "Hidden/Nature/Tree Creator Bark Rendertex"
}
