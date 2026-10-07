using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// Poly Haven HQ 资产铸造：把 Assets/ThirdParty/PolyHaven 下的原始模型/贴图包装成
    /// 【项目自有】的 Built-in RP material / prefab，放进 UnderstorySeeder 的资产门控目录
    /// （Assets/Vegetation/Ferns|Rocks|Deadwood），从而让既有分布逻辑自动生效。
    ///
    /// 硬约束：
    ///  - Assets/ThirdParty/** 只读：不改源文件，也不改源 .meta 的【语义设置】。需要不同导入设置的
    ///    贴图一律复制到项目自有路径后再配置该 copy（当前唯一需要复制的是 Fern 的 nor_gl：
    ///    源 importer 是 textureType=Default + sRGB=true，不能直接当 Normal Map 用）。
    ///  - 唯一的、有界的例外：源 FBX 几何是 Z-up 作者空间 + 米单位，其 Model 节点上的
    ///    R(-90°X)·S(100) 在"直接引用原始 Mesh 子资产"时会被丢掉（见 SourceVertexRotation）。
    ///    读取顶点需要 isReadable=true，因此铸造校正 mesh 时会临时打开源 FBX 的 isReadable 并在
    ///    读完立即恢复为 false；该动作仅发生在【校正 mesh 缺失或过期】时，稳定后重复 Bake
    ///    不再触发任何 reimport / .meta 改写。
    ///  - 像素读取统一走 Editor GPU readback（Blit → RenderTexture → ReadPixels），
    ///    不使用 ImageConversion.LoadImage，也【不临时翻转源 importer 的 isReadable】。
    ///  - 全部产物幂等：已存在且内容正确即复用，重复调用绝不追加重复 prefab / material / texture / mesh。
    ///  - 属性写入逐项比对（一致则跳过），重复 Bake 不产生无谓的资产改写。
    ///
    /// 与既有约定的关系：材质沿用项目 Standard shader 约定（对齐 JurassicWorldSceneBuilder.
    /// LoadOrCreateStandardMaterial）；贴图落盘沿用 EncodeToPNG + File.WriteAllBytes + ImportAsset
    /// （对齐 EnsureGrassTerrainLayer）。仅 fern 使用项目自有双面 Cutout shader。
    /// </summary>
    internal static class PolyHavenAssetIntegration
    {
        // ================================================================ 源路径（只读）

        private const string SourceRoot = "Assets/ThirdParty/PolyHaven";

        private const string FernFbx = SourceRoot + "/Fern02/fern_02_4k.fbx";
        private const string FernDiff = SourceRoot + "/Fern02/textures/fern_02_diff_4k.jpg";
        private const string FernAlpha = SourceRoot + "/Fern02/textures/fern_02_alpha_4k.png";
        private const string FernNormalSource = SourceRoot + "/Fern02/textures/fern_02_nor_gl_4k.exr";
        private const string FernRough = SourceRoot + "/Fern02/textures/fern_02_rough_4k.exr";

        private const string RockFbx = SourceRoot + "/RockMossSet01/rock_moss_set_01_4k.fbx";
        private const string RockDiff = SourceRoot + "/RockMossSet01/textures/rock_moss_set_01_diff_4k.jpg";
        private const string RockNormal = SourceRoot + "/RockMossSet01/textures/rock_moss_set_01_nor_gl_4k.exr";
        private const string RockRough = SourceRoot + "/RockMossSet01/textures/rock_moss_set_01_rough_4k.jpg";

        private const string TrunkFbx = SourceRoot + "/DeadTreeTrunk02/dead_tree_trunk_02_4k.fbx";
        private const string TrunkDiff = SourceRoot + "/DeadTreeTrunk02/textures/dead_tree_trunk_02_diff_4k.jpg";
        private const string TrunkNormal = SourceRoot + "/DeadTreeTrunk02/textures/dead_tree_trunk_02_nor_gl_4k.exr";
        private const string TrunkRough = SourceRoot + "/DeadTreeTrunk02/textures/dead_tree_trunk_02_rough_4k.exr";

        private const string StumpFbx = SourceRoot + "/TreeStump01/tree_stump_01_4k.fbx";
        private const string StumpDiff = SourceRoot + "/TreeStump01/textures/tree_stump_01_diff_4k.jpg";
        private const string StumpNormal = SourceRoot + "/TreeStump01/textures/tree_stump_01_nor_gl_4k.exr";
        private const string StumpRough = SourceRoot + "/TreeStump01/textures/tree_stump_01_rough_4k.exr";

        // ================================================================ 项目自有输出路径

        internal const string TextureFolder = "Assets/Textures/PolyHaven";
        internal const string MaterialFolder = "Assets/Materials/PolyHaven";

        /// <summary>
        /// 校正 mesh 的输出目录：源 FBX 的作者空间几何（Z-up + 米）不能直接当 Unity 资产用，
        /// 校正副本落在这里，prefab 只引用这里（源 FBX 仍是几何的唯一真源）。
        /// </summary>
        internal const string MeshFolder = "Assets/Meshes/PolyHaven";

        /// <summary>Fern 法线贴图的 project-owned copy（源 importer 不可直接用作 Normal Map）。</summary>
        internal const string FernNormalCopyPath = TextureFolder + "/Fern02_Normal.exr";

        internal const string FernAlbedoCutoutPath = TextureFolder + "/Fern02_AlbedoCutout.png";
        internal const string FernSmoothnessPath = TextureFolder + "/Fern02_Smoothness.png";
        internal const string RockSmoothnessPath = TextureFolder + "/RockMossSet01_Smoothness.png";
        internal const string TrunkSmoothnessPath = TextureFolder + "/DeadTreeTrunk02_Smoothness.png";
        internal const string StumpSmoothnessPath = TextureFolder + "/TreeStump01_Smoothness.png";

        private const string FernMaterialPath = MaterialFolder + "/Fern02_Cutout.mat";
        private const string RockMaterialPath = MaterialFolder + "/RockMossSet01.mat";
        private const string TrunkMaterialPath = MaterialFolder + "/DeadTreeTrunk02.mat";
        private const string StumpMaterialPath = MaterialFolder + "/TreeStump01.mat";

        internal const string FernShaderName = "Jurassic World/Foliage Cutout Double Sided";

        /// <summary>fern 材质默认 Cutoff（仅作 material 属性；shader 内不硬编码）。</summary>
        private const float FernCutoff = 0.35f;

        /// <summary>生成贴图的 runtime 导入上限：HQ 资产不无条件以 4K 进 runtime。</summary>
        private const int GeneratedMaxTextureSize = 2048;

        /// <summary>
        /// 生成贴图的【语义版本】：任何会改变像素内容的生成逻辑变更都必须 +1，
        /// 使既有产物在下一次 EnsureAssets 时被自动重建，不留语义过期的贴图。
        ///   v2 —— 读回色彩空间改为与源 importer 的 sRGB 标志匹配（v1 对 sRGB 源多做了一次
        ///          sRGB→Linear 解码，得到 Smoothness = 1 − linearize(Roughness) 而非 1 − Roughness，
        ///          材质会偏亮/偏光滑）。
        /// </summary>
        private const int GeneratedTextureVersion = 2;

        /// <summary>写入 TextureImporter.userData 的版本标记（判定产物是否由当前生成逻辑产出）。</summary>
        private static readonly string GeneratedTextureTag = "polyHavenGen:v" + GeneratedTextureVersion;

        /// <summary>DeadTreeTrunk02 的 LODGroup 固定 screen-relative transition（逐级递减，最低级以下正常 cull）。</summary>
        private static readonly float[] TrunkLodTransitions = { 0.40f, 0.18f, 0.07f, 0.015f };

        // ================================================================ 源几何校正参数

        /// <summary>
        /// 四个源 FBX（校正时需要临时打开 isReadable 的全部对象）。
        /// </summary>
        private static readonly string[] SourceFbxPaths = { FernFbx, RockFbx, TrunkFbx, StumpFbx };

        /// <summary>
        /// 源 FBX 的几何处于【Z-up 作者空间 + 米单位】：FBX 里每个 Model 节点的
        /// Lcl Rotation(-90°, 0, 0) 与 Lcl Scaling(100, 100, 100) 才是"Z-up→Y-up / 米→Unity 单位"
        /// 的转换，其声明头里的 UpAxis 已经是 Y（所以 bakeAxisConversion 无从生效）。
        /// 直接引用 FBX 的原始 Mesh 子资产（挂在单位变换 root 下）会【同时丢掉这两个节点变换】，
        /// 实测后果：岩石平躺的几何被立成竖板，且全部 HQ 资产是真实尺寸的 1/100
        /// （log 实测 5.8 cm，其源 mesh 名为 fallen_log_5m_PROXY）。这里把两者烘进顶点。
        /// </summary>
        private static readonly Quaternion SourceVertexRotation = Quaternion.Euler(-90f, 0f, 0f);

        /// <summary>与 <see cref="SourceVertexRotation"/> 同源的节点缩放（FBX 单位 cm → Unity m）。</summary>
        private const float SourceVertexScale = 100f;

        /// <summary>
        /// 单开关回滚：置 false 即回到"prefab 直接引用源 FBX 原始 mesh"的旧行为
        /// （不再铸造校正 mesh，prefab 内容比对判定为无需变更而保持现状）。
        /// </summary>
        private const bool BakeSourceVertexFix = true;

        // ================================================================ 入口

        [MenuItem("Jurassic World/Assets/Integrate PolyHaven Assets")]
        private static void EnsureAssetsMenuMenu() => JurassicMenuGuard.Run(EnsureAssetsMenu, false);

        [MenuItem("Jurassic World/Assets/Integrate PolyHaven Assets", true)]
        private static bool EnsureAssetsMenuMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void EnsureAssetsMenu()
        {
            if (EnsureAssets())
                Debug.Log("[PolyHaven] 资产铸造完成。");
        }

        /// <summary>
        /// 幂等铸造全部 project-owned 资产（贴图 / 材质 / prefab）。
        /// 源资产缺失时**告警并优雅跳过**（三个资产门控保持 no-op），与 NatureStarterKitLocator 的容错风格一致；
        /// 不抛异常，避免在源资产未导入的机器上直接打断整个 Bake。
        /// </summary>
        internal static bool EnsureAssets()
        {
            string missing = FirstMissingSource();
            if (missing != null)
            {
                Debug.LogWarning($"[PolyHaven] 源资产缺失（{missing}）：跳过 HQ 资产铸造；" +
                                 "Fern / Rock / Deadwood 三个门控保持 no-op。");
                return false;
            }

            JurassicWorldSceneBuilder.EnsureFolder(TextureFolder);
            JurassicWorldSceneBuilder.EnsureFolder(MaterialFolder);
            JurassicWorldSceneBuilder.EnsureFolder(UnderstoryAssetLocator.FernFolder);
            JurassicWorldSceneBuilder.EnsureFolder(UnderstoryAssetLocator.RockFolder);
            JurassicWorldSceneBuilder.EnsureFolder(UnderstoryAssetLocator.DeadwoodFolder);

            // (1) 贴图：Fern 法线 copy + 5 张生成贴图
            int texCreated = 0;
            Texture2D fernNormal = EnsureFernNormalCopy(ref texCreated);
            Texture2D smoothFern = EnsureSmoothnessMap(FernRough, FernSmoothnessPath, ref texCreated);
            Texture2D smoothRock = EnsureSmoothnessMap(RockRough, RockSmoothnessPath, ref texCreated);
            Texture2D smoothTrunk = EnsureSmoothnessMap(TrunkRough, TrunkSmoothnessPath, ref texCreated);
            Texture2D smoothStump = EnsureSmoothnessMap(StumpRough, StumpSmoothnessPath, ref texCreated);
            Texture2D fernAlbedo = EnsureFernAlbedoCutout(ref texCreated);

            if (fernNormal == null || fernAlbedo == null || smoothFern == null ||
                smoothRock == null || smoothTrunk == null || smoothStump == null)
            {
                Debug.LogWarning("[PolyHaven] 贴图产物不完整，中止材质/prefab 铸造（门控保持 no-op）。");
                return false;
            }

            // (2) 材质
            Material fernMat = EnsureMaterial(FernMaterialPath, FernShaderName);
            Material rockMat = EnsureMaterial(RockMaterialPath, "Standard");
            Material trunkMat = EnsureMaterial(TrunkMaterialPath, "Standard");
            Material stumpMat = EnsureMaterial(StumpMaterialPath, "Standard");
            if (fernMat == null || rockMat == null || trunkMat == null || stumpMat == null)
            {
                Debug.LogWarning("[PolyHaven] shader 查找失败（Standard / 双面 Cutout），中止材质铸造。");
                return false;
            }

            // fern：镂空 + 双面 + 法线 + 光滑度（逐项比对，一致则跳过 → 重复 Bake 不产生无谓改写）
            // 刻意不 EnableKeyword(_NORMALMAP/_METALLICGLOSSMAP)：本项目自制 shader 是 surface shader，
            // 由 surf 内直接采样 _BumpMap / _MetallicGlossMap（Standard 光照模型不替你做贴图查找），
            // shader 未声明这两个 keyword，置上只会落进 m_InvalidKeywords（检查器无意义告警）。
            bool fernChanged = SetTextureIfChanged(fernMat, "_MainTex", fernAlbedo);
            fernChanged |= SetTextureIfChanged(fernMat, "_BumpMap", fernNormal);
            fernChanged |= SetTextureIfChanged(fernMat, "_MetallicGlossMap", smoothFern);
            fernChanged |= SetFloatIfChanged(fernMat, "_Glossiness", 1f);
            fernChanged |= SetFloatIfChanged(fernMat, "_Metallic", 0f);
            fernChanged |= SetFloatIfChanged(fernMat, "_Cutoff", FernCutoff);
            fernChanged |= SetInstancingIfChanged(fernMat, true);
            if (fernChanged)
                EditorUtility.SetDirty(fernMat);

            if (ConfigureOpaque(rockMat, Load(RockDiff), Load(RockNormal), smoothRock))
                EditorUtility.SetDirty(rockMat);
            if (ConfigureOpaque(trunkMat, Load(TrunkDiff), Load(TrunkNormal), smoothTrunk))
                EditorUtility.SetDirty(trunkMat);
            if (ConfigureOpaque(stumpMat, Load(StumpDiff), Load(StumpNormal), smoothStump))
                EditorUtility.SetDirty(stumpMat);

            AssetDatabase.SaveAssets();

            // (3) 校正 mesh：源 FBX 原始 Mesh 是 Z-up 作者空间 + 米单位（节点变换被丢弃），
            //     这里把 R(-90°X)·S(100) 烘进顶点，产出 Unity 语义的 project-owned mesh。
            int meshCreated = 0;
            EnsureCorrectedMeshes(ref meshCreated);
            AssetDatabase.SaveAssets();

            // (4) prefab：一律引用【校正后】的 project-owned mesh
            int prefabCreated = 0;
            // Fern：4 个形态各一个 prefab variation（名称含 fern、不含 tree/bush/shrub/grass）
            EnsureSimplePrefab(UnderstoryAssetLocator.FernFolder + "/Fern02_A.prefab", "Fern02_A",
                CorrectedMesh("Fern02_A"), fernMat, ref prefabCreated);
            EnsureSimplePrefab(UnderstoryAssetLocator.FernFolder + "/Fern02_B.prefab", "Fern02_B",
                CorrectedMesh("Fern02_B"), fernMat, ref prefabCreated);
            EnsureSimplePrefab(UnderstoryAssetLocator.FernFolder + "/Fern02_C.prefab", "Fern02_C",
                CorrectedMesh("Fern02_C"), fernMat, ref prefabCreated);
            EnsureSimplePrefab(UnderstoryAssetLocator.FernFolder + "/Fern02_D.prefab", "Fern02_D",
                CorrectedMesh("Fern02_D"), fernMat, ref prefabCreated);

            // Rock：6 个 rock mesh 各一个 variation（名称含 rock、不含 tree/bush/fern/grass）
            for (int i = 1; i <= 6; i++)
            {
                EnsureSimplePrefab(
                    $"{UnderstoryAssetLocator.RockFolder}/RockMossSet01_Rock0{i}.prefab",
                    $"RockMossSet01_Rock0{i}", CorrectedMesh($"RockMossSet01_Rock0{i}"), rockMat, ref prefabCreated);
            }

            // Deadwood：名称【刻意不含 tree】—— 门控 exclude 词含 "tree"，用 TreeStump01 / DeadTreeTrunk02
            // 原名会被静默丢弃。log 名不含 stump、stump 名含 stump（ApplyDeadwoodGate 以此分流）。
            var logLods = new Mesh[TrunkLodTransitions.Length];
            for (int i = 0; i < logLods.Length; i++)
                logLods[i] = CorrectedMesh("LogTrunk02_LOD" + i);
            EnsureTrunkPrefab(trunkMat, logLods, ref prefabCreated);
            EnsureSimplePrefab(
                UnderstoryAssetLocator.DeadwoodFolder + "/DeadwoodStump01.prefab", "DeadwoodStump01",
                CorrectedMesh("DeadwoodStump01"), stumpMat, ref prefabCreated);

            AssetDatabase.SaveAssets();

            Debug.Log($"[PolyHaven] 资产铸造完成：贴图本次新建 {texCreated} / 复用 {6 - texCreated}（1 法线 copy + 4 Smoothness + 1 Cutout Albedo）、" +
                      $"材质 4 个、校正 mesh 本次新建 {meshCreated} / 复用 {15 - meshCreated}（4 Fern + 6 Rock + 4 Log LOD + 1 Stump）、" +
                      $"prefab 本次新建 {prefabCreated} / 复用 {12 - prefabCreated}（4 Fern + 6 Rock + 1 Log + 1 Stump）。");
            return true;
        }

        // ================================================================ 贴图

        /// <summary>
        /// Fern 法线贴图的 project-owned copy：复制源文件后【只改 copy 的 importer】为 Normal Map + sRGB off。
        /// 源 .meta 保持不变。Rock / Trunk / Stump 的 nor_gl 源 importer 已是 Normal Map，直接引用，无需复制。
        /// </summary>
        private static Texture2D EnsureFernNormalCopy(ref int created)
        {
            string absolute = AbsolutePath(FernNormalCopyPath);
            if (!File.Exists(absolute))
            {
                File.Copy(AbsolutePath(FernNormalSource), absolute, false);
                AssetDatabase.ImportAsset(FernNormalCopyPath, ImportAssetOptions.ForceSynchronousImport);
                created++;
                Debug.Log($"[PolyHaven] 建立 Fern 法线 project-owned copy：{FernNormalCopyPath}");
            }

            var importer = AssetImporter.GetAtPath(FernNormalCopyPath) as TextureImporter;
            if (importer != null &&
                (importer.textureType != TextureImporterType.NormalMap ||
                 importer.sRGBTexture ||
                 importer.maxTextureSize != GeneratedMaxTextureSize))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.maxTextureSize = GeneratedMaxTextureSize;
                importer.SaveAndReimport();
                Debug.Log($"[PolyHaven] {FernNormalCopyPath}: Texture Type = Normal Map, sRGB = false, maxTextureSize = {GeneratedMaxTextureSize}（只作用于 copy）。");
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(FernNormalCopyPath);
        }

        /// <summary>
        /// 生成 Smoothness 贴图：RGB = 0（metallic 0），A = 1 − Roughness。
        /// 读取源 roughness 走 GPU readback（Linear），保持源 importer 不变。
        /// </summary>
        private static Texture2D EnsureSmoothnessMap(string roughnessSource, string outputPath, ref int created)
        {
            if (!NeedsRegeneration(outputPath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);

            Texture2D source = Load(roughnessSource);
            if (source == null)
            {
                Debug.LogWarning($"[PolyHaven] 找不到源 roughness：{roughnessSource}");
                return null;
            }

            Color[] src = ReadSourcePixels(source, roughnessSource); // 原始存储值（与源 importer 色彩空间匹配）
            int w = source.width, h = source.height;
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                // 单通道 EXR 经 readback 可能只填 .r，故取 RGB 最大值兜底。
                float roughness = Mathf.Clamp01(Mathf.Max(src[i].r, Mathf.Max(src[i].g, src[i].b)));
                pixels[i] = new Color(0f, 0f, 0f, 1f - roughness); // Smoothness = 1 − Roughness
            }

            WritePng(outputPath, w, h, pixels);
            ConfigureGeneratedImporter(outputPath, srgb: false, alphaIsTransparency: false);
            created++;
            Debug.Log($"[PolyHaven] 生成 Smoothness 贴图 {outputPath}（{w}×{h}，A = 1 − Roughness）。");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }

        /// <summary>
        /// 生成 Fern 的 Cutout Albedo：RGB = diffuse RGB 原样，A = **真实 alpha mask**。
        /// 绝不用 diffuse 亮度参与裁切（深绿色叶片亮度低，会被错误裁掉）。
        /// </summary>
        private static Texture2D EnsureFernAlbedoCutout(ref int created)
        {
            if (!NeedsRegeneration(FernAlbedoCutoutPath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(FernAlbedoCutoutPath);

            Texture2D diffuse = Load(FernDiff);
            Texture2D alphaSource = Load(FernAlpha);
            if (diffuse == null || alphaSource == null)
            {
                Debug.LogWarning($"[PolyHaven] Fern diffuse / alpha 缺失（{FernDiff} / {FernAlpha}）。");
                return null;
            }

            Color[] colorPixels = ReadSourcePixels(diffuse, FernDiff);   // diffuse RGB 原样
            Color[] maskPixels = ReadSourcePixels(alphaSource, FernAlpha); // 真实 alpha mask 原样

            bool maskInAlpha = ProbeMaskChannel(maskPixels, out MaskProbe probe);
            int w = diffuse.width, h = diffuse.height;
            int mw = alphaSource.width, mh = alphaSource.height;

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = colorPixels[y * w + x];
                // 最近邻取样：alpha mask 与 diffuse 导入分辨率可能不同（两者上限均为 2048，通常一致）。
                int mx = mw == w ? x : Mathf.Clamp(x * mw / w, 0, mw - 1);
                int my = mh == h ? y : Mathf.Clamp(y * mh / h, 0, mh - 1);
                Color m = maskPixels[my * mw + mx];
                float mask = maskInAlpha ? m.a : Luminance(m);
                pixels[y * w + x] = new Color(c.r, c.g, c.b, Mathf.Clamp01(mask));
            }

            WritePng(FernAlbedoCutoutPath, w, h, pixels);
            ConfigureGeneratedImporter(FernAlbedoCutoutPath, srgb: true, alphaIsTransparency: true);
            created++;
            Debug.Log($"[PolyHaven] 生成 Fern Cutout Albedo {FernAlbedoCutoutPath}（{w}×{h}）；" +
                      $"alpha mask 来源 = {probe.Describe(maskInAlpha)}。");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FernAlbedoCutoutPath);
        }

        /// <summary>mask 通道探查结果（供日志与 Preview 报告引用，不靠猜）。</summary>
        internal struct MaskProbe
        {
            public float AlphaMin, AlphaMax, RgbMin, RgbMax;
            public int Samples;

            internal string Describe(bool maskInAlpha)
            {
                return (maskInAlpha ? "A 通道" : "RGB 灰度") +
                       $"（A∈[{AlphaMin:F3},{AlphaMax:F3}]，RGB∈[{RgbMin:F3},{RgbMax:F3}]，采样 {Samples} 点）";
            }
        }

        /// <summary>
        /// 定位 alpha mask 实际所在通道：A 通道有变化 → 用 A；A 恒为 1 而 RGB 有变化 → 用 RGB 灰度。
        /// 采样步进以控制开销（4K 源全量 GetPixels 会分配数十 MB）。
        /// </summary>
        private static bool ProbeMaskChannel(Color[] pixels, out MaskProbe probe)
        {
            probe = new MaskProbe { AlphaMin = 1f, AlphaMax = 0f, RgbMin = 1f, RgbMax = 0f };
            int stride = Mathf.Max(1, pixels.Length / 262144); // ≈512² 采样点
            for (int i = 0; i < pixels.Length; i += stride)
            {
                Color c = pixels[i];
                float luma = Luminance(c);
                probe.Samples++;
                probe.AlphaMin = Mathf.Min(probe.AlphaMin, c.a);
                probe.AlphaMax = Mathf.Max(probe.AlphaMax, c.a);
                probe.RgbMin = Mathf.Min(probe.RgbMin, luma);
                probe.RgbMax = Mathf.Max(probe.RgbMax, luma);
            }

            const float varies = 0.02f;
            if (probe.AlphaMax - probe.AlphaMin > varies)
                return true;
            if (probe.RgbMax - probe.RgbMin > varies)
                return false;
            // 两者都无变化：说明 mask 是常量图，此时用 A 通道（保持 alpha 语义）。
            return true;
        }

        internal static bool ProbeFernAlphaMask(out MaskProbe probe)
        {
            probe = default;
            Texture2D alphaSource = Load(FernAlpha);
            if (alphaSource == null)
                return false;
            Color[] pixels = ReadSourcePixels(alphaSource, FernAlpha);
            return ProbeMaskChannel(pixels, out probe);
        }

        /// <summary>
        /// 分析【生成后】的 Fern Cutout Albedo，用于判定「无黑色矩形背景 / 叶缘无大面积缺失 / 深色叶片未被误裁」：
        ///  - TransparentFraction：A &lt; cutoff 的像素占比（≈0 说明未发生裁切 → 会出现矩形背景；过高说明叶缘缺失）；
        ///  - DarkOpaqueFraction：mask 判定为不透明、但亮度 &lt; 0.25 的像素占比 —— 这些是「深色叶片」，
        ///    若用 diffuse 亮度裁切就会被错误裁掉，故该值 &gt; 0 正说明必须用真实 alpha mask。
        /// </summary>
        internal static bool AnalyzeCutoutMask(float cutoff, out float alphaMin, out float alphaMax,
            out float transparentFraction, out float darkOpaqueFraction, out int samples)
        {
            alphaMin = alphaMax = transparentFraction = darkOpaqueFraction = 0f;
            samples = 0;

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(FernAlbedoCutoutPath);
            if (albedo == null)
                return false;

            Color[] pixels = ReadSourcePixels(albedo, FernAlbedoCutoutPath);
            int stride = Mathf.Max(1, pixels.Length / 262144);
            int count = 0, transparent = 0, darkOpaque = 0;
            float min = 1f, max = 0f;
            for (int i = 0; i < pixels.Length; i += stride)
            {
                Color c = pixels[i];
                count++;
                min = Mathf.Min(min, c.a);
                max = Mathf.Max(max, c.a);
                if (c.a < cutoff)
                {
                    transparent++;
                    continue;
                }
                if (Luminance(c) < 0.25f)
                    darkOpaque++;
            }

            samples = count;
            alphaMin = min;
            alphaMax = max;
            transparentFraction = count > 0 ? (float)transparent / count : 0f;
            darkOpaqueFraction = count > 0 ? (float)darkOpaque / count : 0f;
            return true;
        }

        // ================================================================ GPU readback（不改源 importer）

        /// <summary>
        /// 生成贴图是否需要（重新）生成：文件缺失，或产物不是由【当前生成逻辑】产出的
        /// （以 TextureImporter.userData 里的版本标记判定）。
        /// 这样任何改变像素内容的生成逻辑变更都会自动重建既有产物，不留语义过期的贴图。
        /// </summary>
        private static bool NeedsRegeneration(string assetPath)
        {
            if (!File.Exists(AbsolutePath(assetPath)))
                return true;
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            return importer == null || importer.userData != GeneratedTextureTag;
        }

        /// <summary>
        /// Editor GPU readback：Blit 到临时 RenderTexture → ReadPixels 到临时可读 Texture2D。
        /// 源贴图可不可读都成立；RT 与临时纹理在 finally 中释放。
        /// linear 必须与【源 importer 的 sRGB 标志】匹配（见 ReadSourcePixels），否则数据会被 gamma 扭曲。
        /// </summary>
        private static Color[] ReadPixelsFrom(Texture2D source, bool linear)
        {
            int w = source.width, h = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            RenderTexture previousActive = RenderTexture.active;
            Texture2D temp = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                temp = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
                temp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                temp.Apply(false);
                return temp.GetPixels();
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (temp != null)
                    UnityEngine.Object.DestroyImmediate(temp);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// 读取贴图的【原始存储值】，语义与源文件字节一致（不改源 importer，因为它全程只读）。
        ///
        /// 关键：读回用的色彩空间必须与【源 importer 的 sRGB 标志】一致，才能得到恒等变换 ——
        ///   源 sRGB  ：GPU 采样解码 sRGB→Linear，写入 sRGB RT 时再编码 Linear→sRGB ⇒ 净恒等；
        ///   源 Linear：采样不解码，写入 Linear RT 不编码 ⇒ 也恒等。
        /// 若两者不匹配，就会多一次（或漏一次）sRGB 转换，对 roughness / alpha mask 这类
        /// 【数据贴图】会直接算错数值（颜色贴图反而看不太出来）。
        /// </summary>
        private static Color[] ReadSourcePixels(Texture2D source, string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            bool sourceIsSrgb = importer == null || importer.sRGBTexture;
            return ReadPixelsFrom(source, linear: !sourceIsSrgb);
        }

        private static void WritePng(string assetPath, int width, int height, Color[] pixels)
        {
            // CPU 侧统一 linear:true —— 不做任何隐式色彩转换，PNG 原样保存给定字节；
            // 色彩空间完全由【保存后资产的 importer】决定。
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                tex.SetPixels(pixels);
                tex.Apply(false);
                File.WriteAllBytes(AbsolutePath(assetPath), tex.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureGeneratedImporter(string assetPath, bool srgb, bool alphaIsTransparency)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[PolyHaven] 无法取得 {assetPath} 的 TextureImporter。");
                return;
            }

            bool changed =
                importer.textureType != TextureImporterType.Default ||
                importer.sRGBTexture != srgb ||
                importer.alphaSource != TextureImporterAlphaSource.FromInput ||
                importer.alphaIsTransparency != alphaIsTransparency ||
                importer.maxTextureSize != GeneratedMaxTextureSize ||
                importer.wrapMode != TextureWrapMode.Repeat ||
                !importer.mipmapEnabled ||
                importer.userData != GeneratedTextureTag;

            if (!changed)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = alphaIsTransparency;
            importer.maxTextureSize = GeneratedMaxTextureSize; // HQ 纹理不无条件以 4K 进 runtime
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.userData = GeneratedTextureTag; // 版本标记：供 NeedsRegeneration 判定
            importer.SaveAndReimport();
        }

        // ================================================================ 材质

        private static Material EnsureMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[PolyHaven] 找不到 shader \"{shaderName}\"。");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
                Debug.Log($"[PolyHaven] 新建材质 {path}（shader={shaderName}）。");
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>
        /// Standard 不透明材质配置：Albedo / Normal / MetallicGloss(smoothness)。
        /// 代码建材质不会自动置关键字，必须显式 EnableKeyword，否则法线与光滑度贴图完全不生效。
        /// 返回是否有实际改动（一致则返回 false，避免重复 Bake 的无谓资产改写）。
        /// </summary>
        private static bool ConfigureOpaque(Material material, Texture2D albedo, Texture2D normal, Texture2D smoothness)
        {
            bool changed = SetTextureIfChanged(material, "_MainTex", albedo);
            changed |= SetTextureIfChanged(material, "_BumpMap", normal);
            changed |= SetTextureIfChanged(material, "_MetallicGlossMap", smoothness);
            changed |= SetFloatIfChanged(material, "_Metallic", 0f);
            changed |= SetFloatIfChanged(material, "_Glossiness", 1f);              // smoothness 全部来自贴图 A 通道
            changed |= SetFloatIfChanged(material, "_SmoothnessTextureChannel", 0f); // Metallic Alpha
            changed |= EnableKeywordIfMissing(material, "_NORMALMAP");
            changed |= EnableKeywordIfMissing(material, "_METALLICGLOSSMAP");
            changed |= SetInstancingIfChanged(material, true);
            return changed;
        }

        private static bool SetTextureIfChanged(Material material, string property, Texture value)
        {
            if (material.GetTexture(property) == value)
                return false;
            material.SetTexture(property, value);
            return true;
        }

        private static bool SetFloatIfChanged(Material material, string property, float value)
        {
            if (Mathf.Approximately(material.GetFloat(property), value))
                return false;
            material.SetFloat(property, value);
            return true;
        }

        private static bool EnableKeywordIfMissing(Material material, string keyword)
        {
            if (material.IsKeywordEnabled(keyword))
                return false;
            material.EnableKeyword(keyword);
            return true;
        }

        private static bool SetInstancingIfChanged(Material material, bool value)
        {
            if (material.enableInstancing == value)
                return false;
            material.enableInstancing = value;
            return true;
        }

        // ================================================================ prefab

        /// <summary>单 mesh prefab（MeshFilter + MeshRenderer 从零构建：无 Collider、无 Animator、无冗余节点）。</summary>
        private static void EnsureSimplePrefab(string prefabPath, string prefabName, Mesh mesh, Material material, ref int created)
        {
            if (mesh == null)
            {
                Debug.LogWarning($"[PolyHaven] 找不到 mesh（prefab {prefabName}）：跳过。");
                return;
            }

            if (PrefabMatches(prefabPath, mesh))
                return;

            WithPreviewScene(scene =>
            {
                GameObject root = NewInScene(prefabName, scene);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            });

            if (!File.Exists(AbsolutePath(prefabPath)))
            {
                Debug.LogError($"[PolyHaven] prefab 保存失败：{prefabPath} 未生成（mesh={mesh.name}）。" +
                               "若在临时 preview scene 中保存不被支持，需改为 additive 临时场景。");
                return;
            }

            created++;
            Debug.Log($"[PolyHaven] 新建 prefab {prefabPath}（mesh={mesh.name}, tris={mesh.triangles.Length / 3}）。");
        }

        /// <summary>DeadTreeTrunk02：单个 prefab，内部 4 级 LODGroup（固定 transition，最低级以下正常 cull）。</summary>
        private static void EnsureTrunkPrefab(Material material, Mesh[] lodMeshes, ref int created)
        {
            string prefabPath = UnderstoryAssetLocator.DeadwoodFolder + "/DeadwoodLogTrunk02.prefab";

            if (lodMeshes[0] == null)
            {
                Debug.LogWarning("[PolyHaven] DeadwoodLogTrunk02 找不到 LOD0 mesh：跳过 log prefab。");
                return;
            }

            if (TrunkPrefabMatches(prefabPath, lodMeshes))
                return;

            WithPreviewScene(scene =>
            {
                GameObject root = NewInScene("DeadwoodLogTrunk02", scene);
                var group = root.AddComponent<LODGroup>();
                group.animateCrossFading = false;
                group.fadeMode = LODFadeMode.None;

                var lods = new LOD[TrunkLodTransitions.Length];
                for (int i = 0; i < TrunkLodTransitions.Length; i++)
                {
                    GameObject child = NewInScene("LOD" + i, scene);
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = lodMeshes[i] ?? lodMeshes[0];
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material; // 全部 LOD 共用同一 project-owned material
                    lods[i] = new LOD(TrunkLodTransitions[i], new Renderer[] { renderer });
                }

                group.SetLODs(lods); // 自高到低；最低一级以下由 LODGroup 正常 cull
                group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            });

            if (!File.Exists(AbsolutePath(prefabPath)))
            {
                Debug.LogError($"[PolyHaven] prefab 保存失败：{prefabPath} 未生成（LODGroup trunk）。");
                return;
            }

            created++;
            Debug.Log($"[PolyHaven] 新建 prefab {prefabPath}（LODGroup {TrunkLodTransitions.Length} 级，" +
                      $"transition={string.Join("/", TrunkLodTransitions.Select(t => t.ToString("F3")))}, " +
                      $"tris LOD0={lodMeshes[0].triangles.Length / 3}）。");
        }

        // ================================================================ prefab 幂等判定

        /// <summary>
        /// 既有 prefab 是否已指向期望 mesh。早期版本以"文件是否存在"当幂等判定，导致 mesh 被
        /// 修正后既有 prefab 不会被升级；改为比对 MeshFilter 的 mesh 引用（校正 mesh 重建后
        /// GUID 变化 → 引用失效 → 判定为需重建，prefab 自愈）。
        /// </summary>
        private static bool PrefabMatches(string prefabPath, Mesh expectedMesh)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null)
                return false;
            MeshFilter filter = root.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh == expectedMesh;
        }

        /// <summary>LODGroup prefab 是否已指向期望的各级 mesh（与构建时的 null 回退规则保持一致）。</summary>
        private static bool TrunkPrefabMatches(string prefabPath, Mesh[] lodMeshes)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null || root.GetComponent<LODGroup>() == null)
                return false;

            for (int i = 0; i < lodMeshes.Length; i++)
            {
                Transform child = root.transform.Find("LOD" + i);
                MeshFilter filter = child != null ? child.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh != (lodMeshes[i] ?? lodMeshes[0]))
                    return false;
            }
            return true;
        }

        // ================================================================ 源几何校正 mesh

        /// <summary>一条校正 mesh 的规格：输出名 + 源 FBX + 源 mesh 解析 token。</summary>
        private sealed class CorrectedMeshSpec
        {
            public string AssetName;
            public string Fbx;
            public string Token;

            public string Path => MeshFolder + "/" + AssetName + ".asset";
        }

        private static CorrectedMeshSpec[] CorrectedMeshSpecs()
        {
            var specs = new List<CorrectedMeshSpec>();

            for (int i = 0; i < 4; i++)
            {
                string letter = ((char)('a' + i)).ToString();
                specs.Add(new CorrectedMeshSpec
                {
                    AssetName = "Fern02_" + letter.ToUpperInvariant(),
                    Fbx = FernFbx,
                    Token = "fern_02_" + letter
                });
            }

            for (int i = 1; i <= 6; i++)
                specs.Add(new CorrectedMeshSpec
                {
                    AssetName = "RockMossSet01_Rock0" + i,
                    Fbx = RockFbx,
                    Token = "rock0" + i
                });

            for (int i = 0; i < TrunkLodTransitions.Length; i++)
                specs.Add(new CorrectedMeshSpec
                {
                    AssetName = "LogTrunk02_LOD" + i,
                    Fbx = TrunkFbx,
                    Token = "lod" + i
                });

            specs.Add(new CorrectedMeshSpec
            {
                AssetName = "DeadwoodStump01",
                Fbx = StumpFbx,
                Token = "tree_stump_01"
            });

            return specs.ToArray();
        }

        /// <summary>取已铸造的校正 mesh（供 prefab 铸造引用）；未铸造时返回 null。</summary>
        private static Mesh CorrectedMesh(string assetName)
        {
            return AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/" + assetName + ".asset");
        }

        /// <summary>
        /// 铸造/校验全部校正 mesh。
        /// 先在不碰源资产的前提下尝试读顶点；只有 Unity 拒绝读取时才【临时】打开相关源 FBX 的
        /// isReadable，铸造完立即恢复原值 —— 因此稳定后的重复 Bake 既不 reimport 也不改写源 .meta。
        /// </summary>
        private static void EnsureCorrectedMeshes(ref int created)
        {
            if (!BakeSourceVertexFix)
                return;

            CorrectedMeshSpec[] specs = CorrectedMeshSpecs();
            Dictionary<string, Mesh[]> sourceMeshes = LoadAllSourceMeshes();

            // 决策阶段只用非 readable 也能读的元数据（bounds / vertexCount / subMeshCount）。
            var pending = new List<CorrectedMeshSpec>();
            foreach (CorrectedMeshSpec spec in specs)
            {
                Mesh source = FindMesh(sourceMeshes[spec.Fbx], spec.Token);
                if (!IsCorrectedMeshCurrent(AssetDatabase.LoadAssetAtPath<Mesh>(spec.Path), source))
                    pending.Add(spec);
            }

            if (pending.Count == 0)
                return;

            JurassicWorldSceneBuilder.EnsureFolder(MeshFolder);

            // 第一轮：不碰源资产 —— Editor 若允许直接读顶点，则源 .meta 全程不变。
            var deferred = new List<CorrectedMeshSpec>();
            foreach (CorrectedMeshSpec spec in pending)
            {
                if (!ForgeCorrectedMesh(spec, FindMesh(sourceMeshes[spec.Fbx], spec.Token), ref created))
                    deferred.Add(spec);
            }

            if (deferred.Count == 0)
            {
                AssetDatabase.SaveAssets();
                return;
            }

            // 第二轮：仅对第一轮读不到顶点的源临时打开 isReadable，铸造完立即恢复。
            var restore = new List<string>();
            try
            {
                foreach (string fbx in deferred.Select(s => s.Fbx).Distinct())
                {
                    var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
                    if (importer == null || importer.isReadable)
                        continue;
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    restore.Add(fbx);
                }

                // SaveAndReimport 会让先前取到的 Mesh 引用失效，必须重新加载。
                Dictionary<string, Mesh[]> reloaded = LoadAllSourceMeshes();
                foreach (CorrectedMeshSpec spec in deferred)
                {
                    Mesh source = FindMesh(reloaded[spec.Fbx], spec.Token);
                    if (source == null)
                        continue; // 第一轮已告警

                    if (!ForgeCorrectedMesh(spec, source, ref created))
                        Debug.LogWarning($"[PolyHaven] 源 mesh 顶点仍不可读（{source.name}）：跳过校正 mesh {spec.AssetName}，" +
                                         "对应 prefab 将沿用源 FBX 原始 mesh（Z-up 作者空间 + 1/100 尺度）。");
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                foreach (string fbx in restore)
                {
                    var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
                    if (importer != null && importer.isReadable)
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        private static Dictionary<string, Mesh[]> LoadAllSourceMeshes()
        {
            var map = new Dictionary<string, Mesh[]>();
            foreach (string fbx in SourceFbxPaths)
                map[fbx] = LoadMeshes(fbx);
            return map;
        }

        /// <summary>
        /// 铸造一条校正 mesh。返回 false 表示【源顶点读不到】，值得临时打开 isReadable 后重试；
        /// true 表示已产出、或该条目无法通过打开可读性解决（已告警）。
        /// </summary>
        private static bool ForgeCorrectedMesh(CorrectedMeshSpec spec, Mesh source, ref int created)
        {
            if (source == null)
            {
                Debug.LogWarning($"[PolyHaven] 找不到源 mesh（{spec.Token} @ {spec.Fbx}）：跳过校正 mesh {spec.AssetName}。");
                return true;
            }

            Mesh corrected = BuildCorrectedMesh(source, spec.AssetName);
            if (corrected == null)
                return false;

            // 过期产物直接换掉：GUID 变化使 prefab 的 mesh 引用失效，由内容比对判定为需重建而自愈。
            if (AssetDatabase.LoadAssetAtPath<Mesh>(spec.Path) != null)
                AssetDatabase.DeleteAsset(spec.Path);
            AssetDatabase.CreateAsset(corrected, spec.Path);
            created++;
            Debug.Log($"[PolyHaven] 新建校正 mesh {spec.Path}（源 {source.name}：" +
                      $"bounds {source.bounds.size.ToString("F3")} → {corrected.bounds.size.ToString("F3")}，" +
                      $"tris={corrected.triangles.Length / 3}）。");
            return true;
        }

        /// <summary>
        /// 产物是否可用：只使用非 readable 也能读的元数据，判定阶段不必触碰源 FBX。
        /// 覆盖几何包围盒与拓扑；【不含】法线/切线/UV —— 若只改这些通道的生成逻辑，
        /// 需先删除 Assets/Meshes/PolyHaven 再 Bake 才会重建。
        /// </summary>
        private static bool IsCorrectedMeshCurrent(Mesh corrected, Mesh source)
        {
            if (corrected == null || source == null)
                return false;
            if (corrected.vertexCount != source.vertexCount || corrected.subMeshCount != source.subMeshCount)
                return false;

            Vector3 expected = CorrectedSize(source.bounds.size);
            float tolerance = 1e-3f * Mathf.Max(1f, expected.magnitude);
            return (corrected.bounds.size - expected).sqrMagnitude <= tolerance * tolerance;
        }

        /// <summary>校正后的包围盒尺寸：轴序 X→X / Y→Z / Z→Y（R(-90°X) 的几何效果）再乘 100。</summary>
        private static Vector3 CorrectedSize(Vector3 size)
        {
            return new Vector3(size.x, size.z, size.y) * SourceVertexScale;
        }

        /// <summary>
        /// 把 R(-90°X)·S(100) 烘进顶点，产出 project-owned 的 Unity 语义 mesh 副本。
        /// 顶点/法线/切线由旋转+等比正缩放推导（不改手性 → 绕序与切线 w 保持原样），
        /// 其余通道（UV / 顶点色 / 子网格）由 Object.Instantiate 原样复制。
        /// 读不到顶点（源 isReadable=false）时返回 null，由调用方决定是否临时打开可读性重试。
        /// </summary>
        private static Mesh BuildCorrectedMesh(Mesh source, string assetName)
        {
            Vector3[] vertices;
            Vector3[] normals;
            Vector4[] tangents;
            try
            {
                vertices = source.vertices;
                normals = source.normals;
                tangents = source.tangents;
            }
            catch (UnityException)
            {
                return null;
            }

            if (vertices == null || vertices.Length == 0)
                return null;

            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = assetName;
            mesh.hideFlags = HideFlags.None; // 防止从只读源资产继承的标志妨碍落盘/后续编辑

            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = SourceVertexRotation * (vertices[i] * SourceVertexScale);
            mesh.vertices = vertices;

            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = SourceVertexRotation * normals[i];
                mesh.normals = normals;
            }

            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 t = SourceVertexRotation * new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                    tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                }
                mesh.tangents = tangents;
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        // ================================================================ 辅助

        /// <summary>
        /// 在临时 preview scene 内构建并保存 prefab：避免把临时 GameObject 建进当前场景
        /// —— Preview 流程要求【绝不弄脏】生产场景（Main.unity）。
        /// </summary>
        private static void WithPreviewScene(Action<Scene> body)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                body(scene);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static GameObject NewInScene(string name, Scene scene)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static Mesh[] LoadMeshes(string fbxPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Mesh>().ToArray();
        }

        /// <summary>按 精确 → 后缀 → 包含 三级优先级解析 FBX 内 mesh（不硬依赖单一命名格式）。</summary>
        private static Mesh FindMesh(Mesh[] meshes, string token)
        {
            if (meshes == null || meshes.Length == 0)
                return null;

            Mesh exact = meshes.FirstOrDefault(m => string.Equals(m.name, token, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
            Mesh suffix = meshes.FirstOrDefault(m => m.name.EndsWith(token, StringComparison.OrdinalIgnoreCase));
            if (suffix != null)
                return suffix;
            return meshes.FirstOrDefault(m => m.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Texture2D Load(string assetPath)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
                Debug.LogWarning($"[PolyHaven] 找不到贴图：{assetPath}");
            return texture;
        }

        private static string FirstMissingSource()
        {
            string[] required =
            {
                FernFbx, FernDiff, FernAlpha, FernNormalSource, FernRough,
                RockFbx, RockDiff, RockNormal, RockRough,
                TrunkFbx, TrunkDiff, TrunkNormal, TrunkRough,
                StumpFbx, StumpDiff, StumpNormal, StumpRough
            };
            return required.FirstOrDefault(p => !File.Exists(AbsolutePath(p)));
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static float Luminance(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }
    }
}
