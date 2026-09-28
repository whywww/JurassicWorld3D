// 最小化 Built-in RP 双面 Alpha Cutout foliage shader（项目自有，仅材质级使用，不改全局渲染管线）。
//
// 存在理由：Built-in Standard 是 Cull Back。Fern02 是卡片/薄片几何，背面朝相机时会被剔掉，
// 叶片在多数视角下出现空洞。故仅对 fern 使用本 shader：Cull Off + alphatest 镂空。
//
// 光照复用 Unity 内置 `surf Standard` 光照模型（与场景其余 Standard 物体观感一致），
// 因此 surf 内必须【自己】采样 _MetallicGlossMap —— Standard 光照模型只消费
// o.Metallic / o.Smoothness，不会替你做贴图查找（内置 Standard shader 也是在自己的 surf 里查的）。
//
// _Cutoff 只作为 material 属性暴露，绝不在此硬编码具体阈值。
//
// ⚠️ 现状（2026-09-22 探针实测）：fern 作为 terrain detail 绘制的两条路径（Grass / VertexLit）都
// 【不经过本 shader】—— 该材质只提供贴图，着色由 Unity 内置 detail shader 完成（把材质 _Cutoff 置 1
// 画面毫无变化即为证）。因此本 shader 目前不参与运行时渲染，也不承担风摆；蕨类的风动由 detail 系统
// 决定（Grass 模式 = 整株水平平移，已在 UnderstorySeeder 中改为 VertexLit ⇒ 完全静止）。
// 若日后把蕨类改为自研实例化绘制，本 shader 可直接加"按局部高度加权"的顶点风摆（根部会天然固定）。
Shader "Jurassic World/Foliage Cutout Double Sided"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "black" {}
        _Glossiness ("Smoothness", Range(0,1)) = 1.0
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        LOD 200

        CGPROGRAM
        // alphatest:_Cutoff 提供镂空；addshadow 让阴影 pass 使用同一 Cutoff（避免阴影投出整块矩形）。
        #pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff
        // Terrain detail 以实例化绘制，材质 enableInstancing 与关键字必须齐备。
        #pragma multi_compile_instancing
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _MetallicGlossMap;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half _BumpScale;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;

            // Smoothness = 1 - Roughness 已烘焙进 _MetallicGlossMap.a（metallic 为 0 故 .r 无贡献）。
            fixed4 metallicGloss = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = metallicGloss.r * _Metallic;
            o.Smoothness = metallicGloss.a * _Glossiness;

            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_BumpMap), _BumpScale);
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}
