# 蜥结龙行走提速

主场景行走速度由 0.22006987 m/s 调整为 0.44013974 m/s。动画原生地速仍为 0.22006987 m/s，现有同步逻辑因此选择 2 倍步频。步态数据、脚底锁定、模型高度及召唤接近速度不变。

校准和场景生成使用相同的物种速度规则：仅蜥结龙的行走速度为原生地速的两倍。

验证入口：`Jurassic World/Acceptance/Verify Sauropelta Double Walking Speed`，对应批处理方法 `JurassicWorld.Editor.DinosaurGaitAcceptance.RunSauropeltaSpeed`。

该入口覆盖 30、60、144 FPS 下的直行、转向、原地转向、启停、受阻、短步和倒退，并检查两次校准、原生地速、接近速度、模型高度和其他动物配置；直行视频输出至本目录的 `Sauropelta-after.mp4`。

当前状态：编辑器独立编译检查通过。Unity 批处理尚未执行动作验证，也未生成视频：项目原有移动代码缺少 `IsContinuousWalker`、`UpdateContinuousRoaming` 和 `MoveContinuousPath` 定义，编译失败。日志见 `Logs/SauropeltaSpeed.log`。此前 `Reports/ThreeAnimalGaits` 中的验收与视频对应提速前的步态修复，不能作为本次提速的验证结果。
