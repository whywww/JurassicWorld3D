# 雨后初晴环境 — 阶段一

当前仅实施天空、光照、轻微云动和固定曝光的画面收尾。局部雾、光束、远景、湿润材质和声音等待后续逐阶段批准。

## 使用与查收

打开 `Assets/Scenes/Main.unity`，正常 Play；首次以中档检查，用 F10 比较低、中、高档。
不要运行整场 Bake。重新应用当前阶段可使用 `Jurassic World > Environment > Atmosphere > Apply Stage 1 - After Rain`，它只增量更新环境节点。

在出生点抬头旋转观察天空接缝及高光；进入密林检查恐龙与路径辨识度；到东侧小溪检查天空与水面反射；在空地观察阳光与阴影方向。
云动很慢，短暂停留不应感觉天空整体旋转。太阳和原始全景保持固定，叠加云幕沿同一风向轻微漂移。

## 参数与画质

- `Prepared/AfterRain.asset` 为环境参数；天空曝光与相机固定曝光独立。
- HDR 保留 8192 宽的源纹理，沿用项目画质档的纹理缩减设置。
- 中档使用低分辨率 Bloom，高档增加 Bloom 分辨率及云细节；低档关闭 Bloom、减弱云幕。
- 材质在 Play 中实例化，不将动画参数写回项目资产。
- 水面使用同一全景烘焙的静态天空 cubemap；不增加实时反射探针更新。
- 基础环境光强度仍为 0.8；叠加预计算的极弱天空漫反射，三档画质一致，太阳、曝光和 Bloom 保持原值。用户查收后进一步将补光从 5% 降至 3%。此前 10% 候选使地面取样亮度增加约 12.6%，5% 候选增幅约 6.4%；这些测量不代表当前 3% 的实测结果。
- `AfterRain.asset` 的 `skyFillStrength` 控制补光；设为 0 恢复无额外补光。修改后调用环境节点 `RainforestSkyFill.Apply()`，或使用菜单 `Jurassic World > Environment > Atmosphere > Apply Weak Sky Fill`。该菜单只处理补光及对照图，不重跑整场环境、反射或森林生成；不会每帧更新 GI。
- `Prepared/VegetationLighting/` 保存旧 Tree Creator 植被的独立材质及材质引用 prefab 副本；原始网格、地形高度、植被实例位置和导航未重建。
- 植被独立材质修正顶点遮蔽及反射颜色，静止和交互参数一致，叶片实时阴影强度为0.8；全局色调映射不抬升暗部，不使用自发光补光。
- 本阶段保留原有雾设置、主相机裁剪距离、地形、导航和恐龙配置。

## 基线与恢复

本地 `Reports/Atmosphere/StageOne/` 保存本阶段应用前的完整场景快照、全局环境参数、原溪流材质和五个固定机位的编辑态前后截图。
截图用于静态对照，不代表已完成用户手动 Play 查收。

恢复时先备份后续人工改动；以该目录的 `Main.Before.unity` 恢复主场景、以 `CrestCreekWater.Before.mat` 恢复原溪流材质内容，保留目标文件原 `.meta`。
完成树木暗部修正后，如需完全恢复实施前的环境，还应使用 `Reports/Atmosphere/VegetationLighting/Terrain.BeforeMaterialVariants.asset` 恢复原地形数据文件（只为还原原型材质引用），保留其 `.meta`。
这会恢复到阶段一实施前的场景，因此不要在已有后续改动时直接覆盖。
仅临时比较画面时，可禁用主相机的 `RainforestImageFinish`；这只关闭色调映射和 Bloom，不会还原天空与光照。

## 素材来源

- 天空：Kloofendal 48d Partly Cloudy (Pure Sky)，8K HDR。
- 页面：https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky
- 作者：Greg Zaal（原始拍摄）、Jarod Guest（纯天空处理）。
- 许可证：CC0；https://polyhaven.com/license
- `Source/Sky/` 保留用户下载的原始文件，`Prepared/` 为项目加工资源。
- 云幕使用项目自有程序化噪声，无额外外部纹理。

## 下一阶段

阶段二的薄雾、光束软边和噪声使用程序生成纹理，不需要用户额外下载素材。
只有本阶段经用户手动 Play 查收并明确同意后，才开始阶段二。

## 极弱天空补光对照

`Reports/Atmosphere/WeakSkyFill/` 保存森林与恐龙机位的 00、05、10、15 四档历史对照图。当前默认 03，重新进入 Play 后生效；原截图机位 X=14.7、Z=11.5、朝向 175°。检查恐龙背光皮肤、树干和灌木细节，同时确认裸地不过亮、阴影仍有深度。仅停用新增补光可将 `skyFillStrength` 设为 0 并 Apply；不影响植被材质修正。仍等待手动 Play 查收，不进入阶段二。

## 指定树干与灌木局部修正

`Prepared/VegetationLighting/TargetLighting.asset` 分别保存 NSK tree01–04 树干、香蕉树干、六类灌木的材质配置。NSK 树冠叶片维持原设置。树皮使用保留 Tree Creator 风动、交互位移与投影的粗糙 PBR 着色器，恢复对既有环境反射的响应；不添加光源或自发光。

旧打包法线的 G/A 通道是数值数据。`PackedNormals/` 保存关闭 sRGB 的独立副本，仅指定树干及灌木引用；第三方源贴图不变。实例颜色隔离对照未显示主要压暗贡献，因此保留其强度为 1。NSK 树干遮蔽强度为 0.15、反射增益 2（受光面偏黄修正后）；灌木遮蔽强度为 0.35，叶片增益 1.6、枝干增益 2；香蕉树干增益 1.6，其叶片材质保持原值。后续环境材质应用会保留并应用此逐类配置。

`Reports/Atmosphere/TargetVegetation/` 保存各类近景、全景、遮蔽/实例颜色/法线隔离对照，以及运行态查收证据。原截图全景的两处固定地面取样区修正前后像素一致，环境配置和森林布局保持不变。重点手动检查树皮暗面是否保留纹理、反射是否过亮、灌木是否仍有深绿层次，并观察恐龙碰触前后和远近切换。完成后仍停留阶段一，无需新素材。
