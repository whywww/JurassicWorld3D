# 霸王龙 90° 左右转同步修复

2026-10-21。`turn 90 left/right` 恢复原片段约 1.7 秒节奏；朝向、根位移、骨骼动画和入场混合由同一套接受步进驱动。

## 修复

- 连续同方向转身时，将上一动作保留在独立的 `SteppingTurn_Outgoing` 状态中。淡出不会再覆盖或禁用正在播放的新转身；上一动作的脚接触权重也计入混合。
- 动画混合按成功接受的帧推进。转身受阻时保留片段时间和混合权重，恢复时继续原进度；取消 Legacy Animation 自动 CrossFade 与手动权重之间的竞争。
- 截段统一按根旋转修正与动画自身朝向之和判断，插值采样也包含 `authoredYaw`。
- 两份配置均恢复 `turnPlaybackSpeed = 1`：共享 `TRexCuriosity`，以及 Main 和互动测试场景实际引用的 `SideInteractionProfile`。重新校准左右转时不再根据旋转峰值降低播放速度。
- 互动配置还残留旧的离散旋转/位移重建数据，左转相邻 60 Hz 根采样最大跳变约 1.55 米。只重建这两份配置的 90° 左右转根轨迹，沿用平滑的支撑脚重建逻辑；源动画和其他动作数据不变。
- 脚锁的大残差改为按原有每帧预算逐步收敛，避免超过 0.4 米就永远拒绝修正；每帧位移上限和碰撞检查保留。

## 验证

- 运行时及编辑器独立编译通过：`Logs/turn-sync-runtime-compile.log`、`Logs/turn-sync-editor-compile.log`。
- `DinosaurAnimationTimingAcceptance.RunTurns`：30、60、144 FPS 共 87 个场景通过，覆盖 Return、Route、Wander、FrontFollow、FrontReturn，全片段/截段、连续左转/右转、左右交替、入场混合及动作中途被同伴阻挡后恢复。逐帧检查片段时钟、动画启用/权重、实际骨骼水平朝向和根旋转同步。
- 两份配置的左右转曲线均与原片段逐关键帧一致。完整转身耗时为 1.7000 秒，144 FPS 量化后为 1.7014 秒。最大支撑脚误差 0.1583 米，小于现有 0.25 米上限；最大末端朝向误差 0.754°。
- 原有 `DinosaurAnimationTimingAcceptance.Run` 全部 12 项通过，包括两个未修改的近距离咬击动作。
- `TRexFreeRoamSmoke.RunTurns` 的 8 组左右转、静态场景穿越、同伴约束及恢复契约通过；`TRexFreeRoamSmoke.Run` 通过。
- `TRexFrontTestSmoke.Run` 通过；`TRexSideTestSmoke.Run` 全部 14 项通过，含等待、恢复、导航转身和接步行。
- 全路线回归未通过：路线 0/4 组合中，霸王龙在同伴让行状态停滞超过 120 秒。失败时 `IsSteppingTurn=false`、`routeRejoining=false`，并非转身片段停播；本次没有扩大修改会车调度。详见 `Logs/turn-sync-route-final.log`，不能据此宣称全部路线回归通过。
- 独立补跑路线 3/5 组合通过：600 秒模拟行走及转身后接回路线正常，记录到 62 帧转身位于旧路线限制之外。日志：`Logs/turn-sync-route-lastpair.log`。
- 最终回归日志统一位于 `Logs/turn-sync-*-final.log`；完整逐场景结果见 `TurnSynchronization.md`。

## 视频

- `turn-sync-repeat.mp4`：本次 60 FPS 实际 Unity 相机录制，依次为左、左、右、右、左，不重置连续动作之间的角色姿态。
- `turn-sync-repeat.png`：第二次连续左转的六帧截图。
- `turn-sync-comparison.mp4`：先左转、后右转；左侧为保留的既有 8.5 秒录屏，右侧为本次 1.7 秒录屏，完成后保持末帧。背景场景来自不同录制时间，用于动作对比，不作同场景性能比较。
- `Tools/encode_turn_sync_comparison.py` 可重建对比视频和截图。

本次未扩大到 180° 转身、其他恐龙动作或修改森林生成逻辑。工作区同时存在森林扩建工作，验收曾遇到其临时编译错误，待编译恢复后重跑上述最终项目。
