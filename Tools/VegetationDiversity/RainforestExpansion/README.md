# 密林样板相邻圈扩展

本轮只处理样板四周15米：West X[-40,-25] Z[-10,45]，East X[25,40] Z[-10,45]，South X[-40,40] Z[-25,-10]，North X[-40,40] Z[45,60]。边界半开，共4050m²。保留已认可的样板、两个林隙、恐龙启用状态和当前动作碰撞规则；不扩展至整个中心。

交付状态与未通过项见 [Delivery.md](Delivery.md)。布局已保存，普通漫游与支撑脚验收未通过，不能宣称整轮验收通过。

> 验收截图、视频与检查点已清理，文中指向这些文件的链接仅作历史引用。

## 操作

保存场景并退出 Play，使用 `Jurassic World > Environment > Rainforest Expansion`：

- `Build and Verify Ring` 按西、东、南、北依次构建、验证、保存。失败停止后续分区，保留之前通过的分区。
- `Rebuild > West/East/South/North` 单独重建；已生成分区按保存的种植点重放，不重新随机挑选。
- `Remove > West/East/South/North` 单独撤销；`Remove All Expansion` 撤销整圈。保留种植记录，再次构建可复现。

`Assets/Editor/RainforestExpansion.json` 保存冻结外围样方、各区种子、边界、面积分类、启用状态、乔木种植点及各启用恐龙连接路径。新增对象归属 `Rainforest Sample Expansion/<分区>`。原有对象不归入该根，撤销不会删除原树。

先恢复枝叶转换，再构建树干/硬枝数据和导航，最后准备枝叶交互。构建每区验证重复签名、撤销、原对象变换及Terrain树/高度/涂层不变，并检查完整身体平移与拐角净空、既有路线和林隙连通。地面植物不添加硬碰撞。

## Bake

完整场景 Bake 在旧中心加密前恢复记录中的原树，旧播种器避开启用扩展区；样板生成后再重放扩展。恢复的原树仅在缺失时实例化，属于独立保留组，撤销扩展不会删除。这样不会因旧播种器让出区域而丢失该区原有树木。

`VerifyBakeReplay` 在未保存的测试场景中检查扩展整体重放、原树缺失恢复及重复恢复幂等，最后丢弃测试场景。它不重新生成整个地形，不等于执行完整场景 Bake。结果见 `BakeReplay.md`。

## 验收入口与口径

- 普通随机出生、32种子、30/60/144模拟FPS及漫游录像：`JurassicWorld.Editor.ForestPlayAcceptance.RunExpansion`。
- 24组互动与受阻回归：`JurassicWorld.Editor.RainforestSampleAcceptance.RunExpansionMovement`。
- 三机位性能与对照图：`Jurassic World > Diagnostics > Rainforest Expansion > Compare Performance`，批处理 `JurassicWorld.Editor.RainforestSampleAcceptance.RunExpansionPerformance`。基线保留已认可样板，只关闭本轮扩展。
- 既有路线：`JurassicWorld.Editor.DinosaurRouteAcceptance.RunWithoutImages`；霸王龙与伶盗龙相遇：`JurassicWorld.Editor.DinosaurSafetyMainAcceptance.Run`。

普通行走、出生和恢复沿用当前碰撞契约；转身及特殊互动允许静态重叠，同伴身体仍检查。可见网格重叠作为单独诊断，不能把通过报告描述为“所有动画完全不穿树”。模拟FPS不是硬件达到的实际帧率。剑龙当前未启用，不宣称已验证启用剑龙。

性能检查采用同场景、1280×720、相同相机及固定恐龙姿态，交替采样扩展前后；分别报告Main Thread帧耗、Camera.Render提交、绘制数、可用GPU计时。15%目标依据CPU主线程中位值，不以相机提交耗时替代，也不推算发布版动态AI性能。

实施前副本由构建在 `Checkpoint/` 本地生成（Main、样板记录、配置和补丁），不纳入版本库；日常撤销用菜单，不用副本覆盖整个项目。

最终构建、运动和性能状态见本目录各报告；存在失败时以失败报告为准，构建通过不代表运动验收通过。
