# 中心雨林样板

> 验收截图、视频与场景副本已清理，文中指向这些文件的链接仅作历史引用。

路线转身已统一为仅检查同伴身体的表演，允许静态穿插和越过路线边界；转完后以受检查的步行接回路线。此前第三组路线转身卡住的问题已修复，全部三组回归通过，见 [RouteTurnRepair.md](RouteTurnRepair.md)。

本轮尺寸调整、三机位对照及通过／未通过的检查见 [TreeSizingVerification.md](TreeSizingVerification.md)。历史 PlayRepair.md 的通过记录不代表当前全场景回归已通过。

树木尺寸协调见 [TreeSizing.md](TreeSizing.md)。样板使用与外围相同的树种基准高度（tree01/02/03/04：10/9/8/6 米，Pachira_B：6 米），再乘 0.82～1.14 的固定种子变化。`Assets/Editor/RainforestSamplePlanting.json` 保留原有树种、平面位置、朝向及尺寸分位；重建和完整 Bake 优先沿用这些位置，硬枝净空冲突时才采用后续安全候选。该记录在撤销样板后保留，供再次构建复现。尺寸修正前后固定机位对照为 `size-before-0..2.png` / `size-after-0..2.png`。

本轮 Play 穿树与卡死修复、验证结果及限制见 [PlayRepair.md](PlayRepair.md)。

Main 中的 `Central Rainforest Sample` 拥有 X[-25,25]、Z[-10,45] 内本次新增的乔木和林下植被。植物来自现有项目资源。树冠可覆盖通道，叶片沿用现有柔性交互；硬树干与硬枝按高度参与身体扫掠。

## 重建与撤销

- 保存场景并退出 Play，执行 `Jurassic World > Environment > Rainforest Sample > Build and Verify`。流程恢复枝叶转换、移除上次样板、补植、重建树干截面和导航，再准备枝叶交互并保存 Main。
- 执行同组的 `Remove Sample` 只移除本组新增对象，重建导航，并清除启用配置。现有场景对象及 Terrain 树不移位。
- 启用配置为 `Assets/Editor/RainforestSampleEnabled.json`。完整场景 Bake 在中心植被、恐龙身体和导航准备后调用样板播种；旧中心加密播种器让出样板区，后续 Bake 不再启用大范围挪树。
- 首次执行前的场景副本由流程在本地生成（`Main_BeforeSample.unity`），仅作人工比对，不纳入版本库；正常撤销使用上述菜单，不回滚整个场景。

## 动作规则

`Dinosaur Interaction Settings` 集中提供重试间隔（0.5 秒）、静态失败窗口（2 秒）和旧版出口搜索字段。旧半径、方向数与安全出口间隔仅为场景兼容保留，不再用于周期性退路搜索。特殊表演（观察、咆哮、撕咬、180 度转身、等待）只检查其他恐龙的身体，允许穿过树、岩石等静态物体；动画播放、混合、脚步匹配和脚锁保持不变。

真正行走的每一步继续检查树木、实体障碍、活动边界、NavMesh 和其他恐龙。跟随期间记录实际走过的位置与拐点；互动结束后先播放一次 180 度转身，再沿记录的反向路径走回，按实际行走时间结束。回程仅在历史路径缺失、连接失败或被静态障碍阻断时重新规划。特技位移由连接段按行走标准接回历史；表演若把根节点留在 NavMesh 外，随后行走只允许逐步缩短离网格距离，回到网格后再按常规端点检查。无路可走时停走并定期重试，不使用预测未来的出口预留。发现距离、角度、近距离阈值和结束冷却保持原配置。

诊断状态为 `WaitingForClearance`（同类阻挡时显示 `WaitingForPeerClearance`），结束原因直接记录“静态路径不可达”、失去视线或超时。原有同伴优先与让路规则独立处理：动画和行走都不穿透同类身体。表演按正常时间推进，转身保留原有播放倍率；脚锁只限制实际修正量。

共享导航使用较小恐龙的横向净空及高度，为窄通路提供候选路径；最终能否通过由每只恐龙自己的完整方向性身体检查决定。较大的恐龙可能拒绝较小恐龙可用的通路。样板中的两个林隙及连接通道另外预留霸王龙完整动作与步行空间。剑龙的场景启用状态保持原状。

## 验收与报告

普通 Play 回归入口：`Jurassic World > Acceptance > Verify Normal Forest Play`（批处理 `JurassicWorld.Editor.ForestPlayAcceptance.Run`）。它运行实际随机出生和自由漫游，不把恐龙放入预留林隙，也不关闭同伴。先检查 32 个固定种子的运行时身体碰撞包络、导航、相机/同伴间距及六米离开路径，再运行 30 FPS 十分钟、60/144 FPS 各一分钟；结果见 `NormalPlay.md`，前一分钟的追踪录像见 `NormalPlay.mp4`（10 FPS）。实际蒙皮网格与硬木截面的重叠单独汇总为 `VISUAL_ONLY` 诊断，不再单独判失败；出生采样后先刷新动画与蒙皮，普通行走/待机仍严格检查身体包络。自由漫游转身及交互表演允许静态重叠，同伴身体碰撞继续检查；恢复只允许走出尚未退出的既有障碍，不允许进入新障碍。交互中的普通移动不享受表演豁免。历史严格网格判定结果保存在 `NormalPlay.BeforeCollisionPolicy.md`。这与下面的受控互动验收分开报告，旧的 24 组互动通过不能证明普通开局可活动。

`Dinosaur Interaction Settings.spawnSeed` 为 0 时每次 Play 使用新种子，其他值用于复现。控制台 `[SpawnRandomizer] seed=...` 记录实际种子。出生候选必须具备完整身体可走的出口；采样失败时停止该恐龙的初始化并在 HUD 报错，不继续显示未经验证的保存姿态。运行中不会靠瞬移解卡。

霸王龙出生出口与漫游终点不再要求预留静态转身净空；自由漫游转身使用表演碰撞规则。转身后若仍与静态障碍重叠，连续走出后恢复普通避障，没有安全出口时保持恢复状态并每两秒重试。日常转身接回行走继续复用现有脚步相位匹配、0.25 秒混合和首次两次落脚约束。

普通 Play 验收的首分钟连续渲染并录像；后段和 60/144 FPS 检查保持全部恐龙、导航、骨骼、脚锁及枝叶逻辑逐帧运行，只将相机渲染改为每 30 秒取样。要求所有恐龙为 `AlwaysAnimate`，每帧检查身体和支撑脚，并在取样点复核实际网格。此运动测试不用于推算 GPU 或发布版 FPS。

共享导航只生成候选路径；完整身体检查覆盖沿途平移和转角。导航栅格边缘使用附近的导航投影查询连通，物理净空始终检查实际根位置；到达容差不会跳过仍需经过的关键拐点。新增树的硬枝以一米平面分块、半米高度层覆盖木质三角形，避免整层包围圆把枝条之间的空隙一并封闭。

入口：`Jurassic World > Acceptance > Verify Rainforest Sample and Natural Interactions`；原批处理入口 `TRexBlockedInteractionSmoke.Run` 转发到这套替代验收，移除要求穿越静态障碍的旧预期。

- `DensityReport.md`：总面积、林隙、通道、可种植面积、原有与新增树数、外围样方及同口径密度比例。
- `Performance.md`：三个固定机位的 Play 渲染 CPU 提交、帧间隔、绘制数和可用 GPU 计时；截图在测量后采集。帧间隔包含编辑器开销，不代表发布版 FPS。
- `Acceptance.md`：30/60/144 FPS 正面、左右侧及受阻表演、掉头、沿历史回程、同伴让行记录，并检查行走碰撞、静态表演允许穿模、根跳变和支撑脚误差。
- `before-0..2.png` / `after-0..2.png`：入口、林内、侧面相同机位对照；`motion-*.png` 为互动过程采样画面。

Unity 批处理需要正常图形设备。构建入口加 `-quit`；Play 验收入口不加 `-quit`，完成后自行退出。运行本项目现有 `DinosaurRouteAcceptance.RunWithoutImages` 和 `DinosaurSafetyMainAcceptance.Run` 补充路线及霸王龙与伶盗龙相遇回归。

动画时序回归入口：`Jurassic World > Acceptance > Verify Animation Timing`（批处理 `JurassicWorld.Editor.DinosaurAnimationTimingAcceptance.Run`）。在 Main 林隙按 30/60/144 模拟 FPS 检查左右转身和左右咬击的时长、根轨迹、身体净空与支撑脚；报告与对照录像见 `AnimationTimingRepair.md`。
