# 蜥结龙接入

资源来自 `SourceArchives/Dinosaurs/Sauropelta/animated_sauropelta.zip`，原始模型和贴图位于 `Assets/Dinosaurs/Sauropelta/Source`，适配结果位于 `Assets/Dinosaurs/Sauropelta/Prepared`。保留原始 FBX 蒙皮和骨架，`Walk.anim` 为原生 5 秒动作的 Legacy 循环副本；没有新增 Idle 动作。

Main 场景新增一个 `Sauropelta`，体长约 5 米，使用现有小型恐龙自由漫游、导航身体扫掠及霸王龙 15/20 米避让规则。原生步速采样约 0.204 米/秒；实际行走速度设为 0.65 米/秒，动画按约 3.19 倍速播放以匹配位移。停止时冻结当前动作，移动时恢复；没有脚部锁定或召唤互动。

该 FBX 的缩放会被 Unity 的 CPU 蒙皮采样计入，因此编辑器专用的 `SauropeltaMeshSampling` 在转换到世界坐标前消除一次缩放；原生渲染骨架和其他恐龙的采样行为保持原样。身体采样长度约 4.96 米。

菜单：
- `Jurassic World > Scene > Add or Update Sauropelta`：增量接入或更新，不重复添加。
- `Jurassic World > Acceptance > Verify Sauropelta Main Roaming`：重复接入检查、渲染可见性检查、75 秒 Play 测试，检查移动、动作循环、转向、停步冻结和恢复。

验收结果写入 `Logs/sauropelta-acceptance.txt`；截图为 `Logs/sauropelta-model.png`、`Logs/sauropelta-walk.png`、`Logs/sauropelta-stop.png`。验收临时使用固定出生种子，不保存测试种子或禁用输入设置。

人工观察：Main 场景选中 `Sauropelta` 后点击 Play，在 Scene 视图按 F 聚焦。它使用提速后的原生步态，停步没有呼吸等 Idle 动作。
