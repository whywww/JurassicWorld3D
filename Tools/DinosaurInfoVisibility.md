# 恐龙介绍框的目标选择

`DinosaurInfoCard` 在运行时自动接入 `DinosaurVisibility`，复用主场景已有 HUD。地面恐龙与翼龙使用同一套选择规则：

- 屏幕内轮廓至少一半未被遮挡，且有至少 16 个可见采样像素。
- 中心位置 / 可见面积 / 距离的评分权重为 65% / 20% / 15%。
- 屏幕中心、半径为屏幕高度 12% 的区域内，合格地面恐龙优先。
- 挑战目标须领先当前评分 15% 并持续 0.2 秒。当前目标不再合格时立即重新选择。
- 没有有效结果时淡出；超过 0.5 秒未获得更新的结果不再用于显示。

## 遮挡检测

检测相机沿用玩家投影和图层，以 180 像素高度渲染场景原材质的深度。每个候选分别生成受深度遮挡的轮廓和不受遮挡的参考轮廓，比值表示实际可见程度；屏幕外部分不参与分母。叶片镂空、地形和恐龙相互遮挡均由渲染深度处理，不要求碰撞体。

隐藏的渲染回调组件记录检测相机实际选择的 LOD；不会把其他 LOD 模型算进参考轮廓。组件仅运行时附加，不保存进模型或场景。

默认每秒检测 10 次、单次异步读取，不堆积请求；不支持异步读取或读取失败时以每秒 5 次同步检测。相机变更、场景变更和组件禁用会清空结果；仍在读取的缓冲区待回调结束后释放。检测材质和着色器放在 `Assets/Resources`，随构建包含。

## 验证入口

使用 Unity 2022.3 的真实图形设备运行，不能添加 `-nographics`：

```text
Unity.exe -batchmode -force-d3d11 -projectPath E:\Unity\JurassicWorld -executeMethod JurassicWorld.Editor.DinosaurInfoCardAcceptance.Run -logFile E:\Unity\JurassicWorld\Logs\info-card-visibility.log
Unity.exe -batchmode -force-d3d11 -projectPath E:\Unity\JurassicWorld -executeMethod JurassicWorld.Editor.DinosaurInfoCardAcceptance.RunMain -logFile E:\Unity\JurassicWorld\Logs\info-card-main-play.log
```

入口会自行退出批处理进程，不应添加 `-quit`，因为异步读取和 Play Mode 验收需要后续帧。不能同时打开同一个 Unity 项目。测试不保存场景。

`Run` 覆盖评分、核心区优先、遮挡比例、叶片镂空、地形、相互遮挡、真实翼龙、异步读取及资源释放。
`RunMain` 在主场景 Play Mode 采集玩家起点、地面恐龙、密林和翼龙视角，比较检测开关前后的帧耗时，输出截图及 `Logs/InfoCard/main-performance.md`。编辑器帧耗时不能代替独立构建的硬件性能；GPU 计时未由设备提供时明确标注 unavailable。
