# 包鲁鳄持续行走与纯走路循环

已保存主场景和生成入口。只在 **144 FPS 模拟步长**下验证，没有运行 30 或 60 FPS 验证。

- 原动画保留 **0.500–1.850 秒**，生成 **1.350 秒**的纯走路循环，去掉后段张嘴及伴随动作；原始 FBX 未修改。
- 保留全部 630 个动画通道，只截取和平移时间，没有重做四肢动作或调整动作节奏。原曲线对照最大分量误差为 **1.4901×10⁻⁷**；导出后各骨骼最大循环角度接缝为 **0.289°**。
- 当前直行速度 **0.6022533 m/s**、模型高度、腿链和脚底偏移保持不变。脚部接触曲线同步截取。
- 取消出生和到达目标后的主动休息，提前接续目标，转向沿经过导航、身体和安全距离检查的弧线持续前进。
- 24°/s 的左右转弯通过落脚检查，最大选用档位的锚点误差约 **6.77 毫米**，低于 20 毫米上限；30°/s 未采用。

## 验证结果

十个完整动画循环、直行和左右转弯通过，无新增腿段拉伸或起脚跳变。主场景检查中，正常行进 10 秒移动 6.023 米，完成一次目标衔接，休息帧数为 0；弧线转向时保持向前移动。临时围挡使其安全等待，撤除后在下一帧（约 0.0069 秒）恢复行走。

[动画、曲线和落脚数据](E:/Unity/JurassicWorld/Reports/BaurusuchusContinuousWalk/Motion144.md) · [主场景运行记录](E:/Unity/JurassicWorld/Reports/BaurusuchusContinuousWalk/Main144.md)

## 对照视频

[原完整动画，包含张嘴动作](E:/Unity/JurassicWorld/Reports/BaurusuchusContinuousWalk/before.mp4) · [新的纯走路循环，连续十次](E:/Unity/JurassicWorld/Reports/BaurusuchusContinuousWalk/after.mp4)

视频使用相同跟随机位，编码为 24 FPS；所有动作推进和测量均使用 144 FPS 的时间步长。

运行时和编辑器编译检查通过。重新生成后，循环长度、当前速度和模型高度保持一致；其他动物的行为没有改动。

编辑器入口：`Jurassic World/Acceptance/Repair Baurusuchus Continuous Walk (144 FPS)` 和 `Jurassic World/Acceptance/Verify Baurusuchus Continuous Walk in Main (144 FPS)`。

必要的安全等待仍然保留；没有可通行路径时不会强行穿过障碍物或其他动物。
