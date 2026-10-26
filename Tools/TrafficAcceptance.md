# 交通交叠与紧急脱困验收

2026-10-26（Asia/Hong_Kong），Unity 2022.3.62f3c1，当前工作区，Windows batchmode / nographics。固定步长用于行为测试，不是渲染性能测量。

## 修正内容

原交通验收把紧急脱困途中允许的身体穿透判为普通交通失败，在 `initial circle overlap` 提前停止。运行时规则保持原样：连续 8 秒无有效位移后放弃旧任务，脱困途中允许穿过静态障碍和同伴，终点仍须安全，完成后恢复碰撞检查。

`DinosaurAgent.EmergencyEscapeMoveCount` 是每个实例的只读、非序列化计数，只在提交脱困位移时递增。验收比较相邻采样的计数：只有交叠双方之一在该区间实际移动，才豁免该区间加深的交叠；进入脱困、搜索出口、等待或过去曾脱困均不授予例外。计数覆盖结束脱困的最后一步，不依赖采样时的 `IsEmergencyEscaping`。

正常移动仍使用原来的 0.0001 米交叠增长容差。每次检查后更新全部计数与交叠基线，例外不会跨区间继承。脱困完成须退出脱困状态并消除身体交叠，单列为 `EMERGENCY_FALLBACK`，不能算作 `NORMAL_CLEARANCE`；需要恢复原目标的案例一旦进入脱困仍失败。一个案例失败后继续其余案例，总退出码保留失败。

## 本次结果

|检查|结果|日志或报告|
|---|---|---|
|紧急脱困专项|通过，退出码 0；全 9 种地面物种及新例外回归|`Logs/traffic-exception-emergency.log`|
|完整交通验收|14/14 项执行，正常解围 2、紧急脱困完成 9、失败 3；退出码 1|`Logs/ground-traffic-acceptance.txt`、`Logs/traffic-exception-ground.log`|
|恐龙安全|失败，退出码 1；两秒内未实际后撤|`Logs/dinosaur-safety-acceptance.txt`、`Logs/traffic-exception-safety.log`|
|霸王龙预约／历史返程|通过，退出码 0；开放跟随、表演／返程碰撞恢复、同伴优先级|`Logs/trex-reservation-result.txt`、`Logs/traffic-exception-reservation.log`|
|Main 场景安全|通过，退出码 0；相遇 3.13 秒释放，联动返程 5.62 秒恢复实际漫游|`Logs/dinosaur-main-encounter-result.txt`、`Logs/traffic-exception-main-safety.log`|

交通分类：

- 正常解围：`crossing`、`overtaking`。
- 紧急脱困完成：`head-on`、`standing blocker`、`initial circle overlap`、`three-way chain`、`narrow passing bay`、`solid beside retreat`、`blocked turn`、`rex safety`、`initial body overlap`。初始圆重叠记录 5 个交叠增长例外采样，三方会车记录 2 个；其他案例也发生过脱困，不能仅凭未发生穿透标为正常解围。
- 失败：`no exit then obstacle removed` 在移除障碍后 3 秒内未恢复；`resume original goals` 和 `retreat to wider section` 进入放弃旧任务的紧急脱困，未满足原目标恢复要求。

紧急脱困专项实际执行同伴穿越，验证位移计数逐步递增、交换交叠双方顺序、结束脱困的最后一步，以及正常碰撞恢复。额外注入交叠增长，确认无新位移计数时会失败，即使当前正在脱困或过去已完成脱困；找不到出口时计数保持零。没有修改防穿透阈值或用扩大例外使独立失败通过。

## 复现

在项目根目录使用匹配版本 Unity，逐项运行，等待每个进程结束；Play 测试不加 `-quit`，由验收器返回退出码。例如：

```powershell
$unity = 'C:/Program Files/Unity 2022.3.62f3c1/Editor/Unity.exe'
$project = (Get-Location).Path
$check = Start-Process -FilePath $unity -ArgumentList "-batchmode -nographics -projectPath `"$project`" -executeMethod JurassicWorld.Editor.DinosaurGroundTrafficAcceptance.Run -logFile `"$project/Logs/traffic-exception-ground.log`"" -WindowStyle Hidden -PassThru -Wait
$check.ExitCode
```

其余入口依次为 `JurassicWorld.Editor.DinosaurEmergencyEscapeAcceptance.Run`、`JurassicWorld.Editor.DinosaurSafetyAcceptance.Run`、`JurassicWorld.Editor.TRexReservationSmoke.Run`、`JurassicWorld.Editor.DinosaurSafetyMainAcceptance.Run`，使用不同日志名。原始日志位于忽略的 `Logs/`，不随仓库提交。

本轮仅修正验收与既有脱困规则的冲突。上述独立失败仍需后续修复；没有改场景、模型、物种优先级或八秒紧急穿透行为。
