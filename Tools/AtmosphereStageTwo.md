# 雨后初晴 — 阶段二

状态：阶段二实施；等待用户手动 Play 查收。未开始阶段三。

## 改动范围

- 在当前主场景上增量加入 `Rainforest Atmosphere/Stage 2 - Creek Air and Sunlight`。
- 使用已有 Creek Flow Point 的位置，建立 7 处低速薄雾。
- 东侧溪流、溪流中段和北侧各放置 1 处与太阳方向一致的柔和光束。
- 距离雾由 60–180 米调整为 42–155 米，颜色偏灰蓝绿，近景保持清晰。
- 保留现有天空、曝光、树皮及植被明暗修正、地形、导航、小溪和恐龙配置。
- 雾纹理和光束软边由程序生成，无外部图片或新增插件。

## 查收位置与关注点

打开 `Assets/Scenes/Main.unity`，直接 Play，无需 Bake。先用中档，再按 F10 比较高档与低档。

1. 出生点附近往东侧小溪走：薄雾应贴近溪流、缓慢漂移，不遮住道路或形成白墙。
2. 沿溪往北：检查中段弯道和北段的雾是否自然衔接，有无规则重复图案。
3. 走进、走出薄雾：近处淡出应平顺，不应突然整屏变白；地形、石头边缘不应被硬切。
4. 绕溪边光束侧面观察，再抬头：光束应柔和、同太阳方向，不应像硬纸片或实体柱子。
5. 看小地图和水面：不应有重复雾层或光束几何。
6. 低档关闭局部雾和光束，但保留基础距离雾；中档粒子较少，仅启用前两处光束；高档开放完整分布，远处效果仍会被距离限制。

## 参数与成本

`Assets/Environment/Atmosphere/Prepared/AfterRain.asset` 的 Stage 2 区域保存距离雾、薄雾和光束强度。
`localAtmosphereEnabled` 是后续重建/校验的阶段标记；临时隐藏局部效果请在编辑态禁用阶段二根节点，距离雾仍沿用保存的场景设置。

中档最多 42 个粒子，高档最多 84 个粒子；每处光束为 160 个三角形的轻量锥壳，使用视角/边缘/近距离/深度淡出。
这是一种轻量视觉近似，并非完整体积散射或逐像素阴影体积光。
粒子不碰撞、不投影；远处暂停模拟，摄像机深度在 Play 时请求。
小地图和 Crest 平面反射排除 Atmosphere 图层，运行时不重新搜索森林或溪流路径。

## 验证和本地报告

`Reports/Atmosphere/StageTwo/` 保存本阶段实施前的场景、环境配置和雾参数，以及同机位前后截图与运行态截图。
`Logs/atmosphere-stage2.log` 为实际 Unity 应用与着色器检查日志。
`Logs/atmosphere-stage2-smoke.log` 与 `Reports/Atmosphere/StageTwo/Checks.txt` 为短暂 Play 功能检查结果。
检查覆盖重复应用、三档效果开关、软交界/雾内/侧视截图、相机图层排除和退出 Play 后文件无漂移；不包含性能路线或演示机测试。
最终视觉查收仍需用户手动 Play。

重新增量应用可用 `Jurassic World > Environment > Atmosphere > Apply Stage 2 - Creek Air and Sunlight`。
不要使用整场 Bake 来应用本阶段。全量场景构建只有在已有溪流路径存在时才能恢复此阶段；缺少溪流时会明确中止，避免保存缺失效果的半成品。
回退时先保存后续人工改动，再用本报告目录的 `Main.Before.unity` 和 `AfterRain.Before.asset` 恢复主场景与环境配置的内容，保留目标文件原 `.meta`；本阶段不修改植被材质或地形/导航资产。

## 下一阶段需要手动准备的素材

首选：Coastal Cliff 01（Poly Haven），页面提供预览和模型下载。

- 页面：https://polyhaven.com/a/coastal_cliff_01
- 下载：FBX（ZIP），4K 贴图；保留全部贴图与提供的 LOD，不需要 Blender/8K 版本。
- 作者：Rico Cilliers（Cleanup）、Rob Tuytel（Photography, Processing）。
- 许可证：CC0；https://polyhaven.com/license 。无强制署名，项目仍记录来源。
- 放置：`Assets/Environment/Backdrop/Source/CoastalCliff01/`，解压并保持原目录结构。
- 用途：远景岩壁与山脊轮廓；最高精度模型不会直接作为最终背景预算使用，整合时采用低 LOD 或简化版本。

备选：Cliff Rock Boulder Field（Pers Scans），https://sketchfab.com/3d-models/cliff-rock-boulder-field-d1f2ce6f71aa4c19adbdc541cc888194 。
检索到的页面信息列有 FBX、OBJ、Blender 与 8K 贴图，许可证为 CC Attribution；直接页面读取返回 403，需用户浏览器确认当前下载按钮和许可，因此不作为必须下载项。采用时须保留作者、作品链接和署名记录。

本轮只需要首选的一份模型。近层树冠复用已有树木。
准备素材不代表批准下一阶段；等待用户完成本阶段查收并明确同意后，再实施远景。
