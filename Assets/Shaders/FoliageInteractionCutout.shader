Shader "Jurassic World/Interactive Cutout"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _AlbedoGain ("Albedo Gain", Range(1,4)) = 1
        _InstanceTintStrength ("Instance Tint Strength", Range(0,1)) = 1
        [HideInInspector] _TreeInstanceColor ("Tree Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _AlphaTex ("Separate Opacity", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = .35
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _MetallicGlossMap ("Metallic Smoothness", 2D) = "white" {}
        _HasMetallicGlossMap ("Use Metallic Smoothness", Float) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = .15
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        _EmissionMap ("Emission Map", 2D) = "white" {}
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
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert alphatest:_Cutoff
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "FoliageInteractionStandard.cginc"
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}

