> 本文记录较早的三种动物滑步修复。包鲁鳄后续已改为纯走路循环和持续前进，最新结果见 [包鲁鳄持续行走验收](E:/Unity/JurassicWorld/Reports/BaurusuchusContinuousWalk/README.md)。

# 三种动物滑步修复验收

主场景已保存。63 个修复后动作场景通过，另保留 3 个修复前基线；主场景以 30、60、144 FPS 模拟步长各运行 45 秒并通过。

|动物|自然行进速度 m/s|修复前最大水平滑移 mm|修复后最大水平滑移 mm|对照视频|
|---|---:|---:|---:|---|
|蜥结龙|0.220|76.97|0.46|[修复前](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/Sauropelta-before.mp4) · [修复后](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/Sauropelta-after.mp4)|
|厚头龙|1.126|378.69|0.64|[修复前](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/Pachycephalosaurus-before.mp4) · [修复后](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/Pachycephalosaurus-after.mp4)|
|包鲁鳄|0.602|191.35|0.22|[修复前](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/Baurusuchus-before.mp4) · [修复后](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/Baurusuchus-after.mp4)|

修复后动作采样的最大锚点误差为 0.83 毫米；主场景最大锚点误差为 0.58 毫米，均低于 20 毫米验收限值。

动作覆盖直行三个完整循环、缓转、原地踏步转向、启停、受阻、短步、倒退及循环接缝；同时检查脚底参考点不穿地、腿段长度不变和起脚释放。重复校准两次后，模型高度和地速保持一致。

正常直行采用约 1 倍原生步频。为保持支撑脚可达，转向限制为每秒 2 度；原地转向继续踏步。蜥结龙停步冻结，厚头龙使用 Idle，包鲁鳄站定仍播放 Run。霸王龙和迅猛龙在主场景中继续使用原有脚部模式。

[逐动作测量表](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/Acceptance.md) · [主场景运行记录](E:/Unity/JurassicWorld/Reports/ThreeAnimalGaits/MainPlay.md)

运行时和编辑器独立编译检查通过。额外尝试的旧路线交叉回归脚本未能编译：其模拟 DinosaurAgent 缺少生产代码已有的 UsesRouteScheduling 属性；该旧测试文件未修改。

修复入口：`Jurassic World/Scene/Repair Three Animal Gaits`。生成动物也会调用相同校准逻辑，无需重建整个场景。

脚底曲线、腿链与速度数据位于三者各自的 `Prepared/Gait.asset`；修复前配置保存在 `Baseline.json`，视频使用相同跟随机位和地面网格。
