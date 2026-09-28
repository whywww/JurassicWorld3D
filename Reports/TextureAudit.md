# 纹理资源审计

由 Unity Editor 的 `Jurassic World/Export Texture Audit` 导出。分辨率与格式来自当前导入结果；运行内存是 `Profiler.GetRuntimeMemorySizeLong` 的估值，并非 GPU 显存实测或场景实际驻留量。

- 纹理数量：178
- 源文件长边至少 4096 像素：15
- 导入后长边超过 2048 像素：0
- 这些纹理源文件合计：456.9 MiB
- 全部导入纹理运行内存估值合计：275.8 MiB（并非同一时刻全部加载）

| 资源 | 源分辨率 | 导入分辨率 | 格式 | Mip Map | 估值 MiB |
|---|---:|---:|---|---|---:|
| Assets/Materials/NatureSkyPanorama.tga | 2048×2048 | 2048×2048 | R8G8B8_SRGB | 否 | 12.0 |
| Assets/Resources/Dinosaurs/Velociraptor/Raptor/Textures/Raptor_Normal.png | 2048×2048 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/Resources/Dinosaurs/Velociraptor/Raptor/Textures/Raptor_NormalDetailed.png | 2048×2048 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/Textures/PolyHaven/DeadTreeTrunk02_Smoothness.png | 2048×2048 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/Textures/PolyHaven/Fern02_AlbedoCutout.png | 2048×2048 | 2048×2048 | RGBA_DXT5_SRGB | 是 | 5.3 |
| Assets/Textures/PolyHaven/Fern02_Normal.exr | 4096×4096 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/Textures/PolyHaven/Fern02_Smoothness.png | 2048×2048 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/Textures/PolyHaven/RockMossSet01_Smoothness.png | 2048×2048 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/Textures/PolyHaven/TreeStump01_Smoothness.png | 2048×2048 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/ThirdParty/PolyHaven/DeadTreeTrunk02/textures/dead_tree_trunk_02_nor_gl_4k.exr | 4096×4096 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/ThirdParty/PolyHaven/DeadTreeTrunk02/textures/dead_tree_trunk_02_rough_4k.exr | 4096×4096 | 2048×2048 | RGB_BC6H_UFloat | 是 | 5.3 |
| Assets/ThirdParty/PolyHaven/Fern02/textures/fern_02_nor_gl_4k.exr | 4096×4096 | 2048×2048 | RGB_BC6H_UFloat | 是 | 5.3 |
| Assets/ThirdParty/PolyHaven/Fern02/textures/fern_02_rough_4k.exr | 4096×4096 | 2048×2048 | RGB_BC6H_UFloat | 是 | 5.3 |
| Assets/ThirdParty/PolyHaven/RockMossSet01/textures/rock_moss_set_01_nor_gl_4k.exr | 4096×4096 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/ThirdParty/PolyHaven/TreeStump01/textures/tree_stump_01_nor_gl_4k.exr | 4096×4096 | 2048×2048 | RGBA_DXT5_UNorm | 是 | 5.3 |
| Assets/ThirdParty/PolyHaven/TreeStump01/textures/tree_stump_01_rough_4k.exr | 4096×4096 | 2048×2048 | RGB_BC6H_UFloat | 是 | 5.3 |
| Assets/NatureStarterKit2/Textures/ground01.psd | 2048×2048 | 2048×2048 | RGBA_DXT1_SRGB | 是 | 2.7 |
| Assets/NatureStarterKit2/Textures/ground01.tga | 2048×2048 | 2048×2048 | RGBA_DXT1_SRGB | 是 | 2.7 |
| Assets/NatureStarterKit2/Textures/ground02.tga | 2048×2048 | 2048×2048 | RGBA_DXT1_SRGB | 是 | 2.7 |
| Assets/NatureStarterKit2/Textures/ground03.psd | 2048×2048 | 2048×2048 | RGBA_DXT1_SRGB | 是 | 2.7 |

完整清单及导入最大尺寸、压缩设置、UI 标记、源文件大小见 `TextureAudit.csv`。当前 4K 源图已在导入时缩至不超过 2K；仅改源文件分辨率不会进一步降低游戏运行时纹理占用。占用最大的天空图未启用 Mip Map，属全景天空用途，应单独评估。高档保持当前资源外观。

