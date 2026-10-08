Shader "Jurassic World/Interactive Tree Leaves"
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
        [HideInInspector] _TreeInstanceColor ("Tree Color", Vector) = (1,1,1,1)
        [HideInInspector] _TreeInstanceScale ("Tree Scale", Vector) = (1,1,1,1)
        [HideInInspector] _SquashAmount ("Squash", Float) = 1
        [HideInInspector] _FoliageActive ("Active", Float) = 0
        [HideInInspector] _LeafDisplacement ("Leaf Displacement", Float) = .45
        [HideInInspector] _BranchDisplacement ("Branch Displacement", Float) = .2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        LOD 200
        CGPROGRAM
        #pragma surface surf TreeLeaf fullforwardshadows addshadow vertex:vert alphatest:_Cutoff
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityBuiltin3xTreeLibrary.cginc"
        #include "FoliageInteraction.cginc"
        sampler2D _MainTex, _BumpSpecMap, _TranslucencyMap;
        struct Input { float2 uv_MainTex; fixed4 color : COLOR; };
        void vert(inout appdata_full v)
        {
            TreeVertLeaf(v);
            ApplyFoliageInteraction(v);
        }
        void surf(Input IN, inout LeafSurfaceOutput o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex);
            fixed4 packed = tex2D(_BumpSpecMap, IN.uv_MainTex);
            fixed4 trans = tex2D(_TranslucencyMap, IN.uv_MainTex);
            o.Albedo = albedo.rgb * IN.color.rgb * IN.color.a;
            o.Alpha = albedo.a;
            // Tree Creator packs specular in R, so its normal must be decoded from AG only.
            o.Normal = UnpackNormalDXT5nm(packed);
            o.Specular = packed.r;
            o.Gloss = trans.a;
            o.Translucency = trans.b;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}

