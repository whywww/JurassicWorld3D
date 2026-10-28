Shader "Jurassic World/Low Cheap TreeLeaves"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpSpecMap ("Normal Specular", 2D) = "bump" {}
        _TranslucencyMap ("Translucency Gloss", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = .3
        _TranslucencyColor ("Translucency", Color) = (.73,.85,.41,1)
        _TranslucencyViewDependency ("Translucency View", Range(0,1)) = .7
        _ShadowStrength ("Shadow Strength", Range(0,1)) = .8
        _OcclusionStrength ("Baked Occlusion Strength", Range(0,1)) = 1
        _AlbedoGain ("Albedo Gain", Range(1,4)) = 1
        _InstanceTintStrength ("Instance Tint Strength", Range(0,1)) = 1
        _NormalStrength ("Normal Strength", Range(0,1)) = 1
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
        Tags { "RenderType"="TreeLeaf" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        LOD 200
        CGPROGRAM
        #pragma surface surf TreeLeaf fullforwardshadows addshadow vertex:vert alphatest:_Cutoff
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityBuiltin3xTreeLibrary.cginc"
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
        sampler2D _MainTex, _BumpSpecMap, _TranslucencyMap;
        half _OcclusionStrength, _AlbedoGain, _InstanceTintStrength, _NormalStrength;
        struct Input { float2 uv_MainTex; fixed4 color : COLOR; };
        void vert(inout appdata_full v)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            TreeVertLeaf(v);
            v.color.rgb = _Color.rgb * lerp(half3(1,1,1), LowTreeColor().rgb, _InstanceTintStrength);
        }
        void surf(Input IN, inout LeafSurfaceOutput o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = saturate(albedo.rgb * IN.color.rgb * _AlbedoGain) * lerp(1, IN.color.a, _OcclusionStrength);
            o.Alpha = albedo.a;
            // Tree Creator packs specular in R, so its normal must be decoded from AG only.
            o.Normal = half3(0,0,1);
            o.Normal = normalize(lerp(half3(0,0,1), o.Normal, _NormalStrength));
            o.Specular = 0;
            o.Gloss = 0;
            o.Translucency = .15;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
    Dependency "BillboardShader" = "Hidden/Nature/Tree Creator Leaves Rendertex"
}

