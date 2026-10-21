# Follow 出口维护与 Step4 安全离场

## 行为与配置

Follow 保留一条完整验证过的退出路径，允许先沿跟随路线前进，再从连接点离开。当前位置不能完成 180° 转身，不再直接结束互动。Step4 从 Ending 的实际结束姿态重新验证连接；优先直行和小角度转向，不要求返回原地点或播放固定的 180° 动作。

|参数|默认值|
|---|---|
|出口复核间隔|0.5 秒|
|前方预测窗口／采样间距|8 米／2 米|
|提前复核阈值|路线偏离 1 米，或朝向改变 15°|
|候选角度|0°、±15°、±30°、±45°、±60°、±90°、±135°、180°|
|候选距离|4、8、12、16 米|
|每帧出口路径查询上限|2 次 CalculatePath|
|每帧几何验证批次上限|32 个姿态检查|
|规划服务时间预算|0.5 毫秒软预算|
|无解重试／安全待机重试|0.5 秒／2 秒|

参数集中在 `DinosaurInteractionSettings`。此版没有加入 1 秒／0.25 秒自适应频率。原有运动防碰撞、导航射线和正常 Follow 导航重算继续运行；上述查询上限约束新增出口规划服务。

## 空间与动作

- 身体、髋部、颈部和头部参与硬障碍检测，XZ 每侧余量 0.05 米；尾巴、四肢不扩大互动空间要求。四肢地面和脚锁检查保留。
- 当前运动继续逐帧扫掠。转身使用对应时刻的包络，混合只保留实际过渡所需的姿态；不旋转整段动画合并后的大包络。
- 先验证前进路线，再搜索离场路径；共同前缀只验证一次。常规两米采样之间遇到阻挡时，保留最后一个扫掠安全的位置作为额外连接候选。
- 目标点还要通过后续四米步行及可换向空间验证，不能仅依赖 NavMesh 连通。这里的换向用于证明目标可继续漫游，不代表 Step4 必须执行该动作。
- 相同角度的左右候选比较路径长度；完全相同时保持上次方向。有效旧方案继续复用，连接、障碍或跟随方向失效才重算。
- Ending 的预检和执行持续保留可衔接行走的姿态。静态空间不足时跳过不安全结束动作；同伴阻挡单独等待。动画资源、播放倍率、根位移采样、互动距离和冷却均保持。
- Step4 不追回已经走过的路径点。到达终点附近时从实际姿态重新验证后续步行，再交接漫游；不瞬移，不为几厘米误差再启动大转身。

## 调试与复现

F4 窗口显示当前阻挡、出口规划状态、候选方向、连接点、出口预测失败和历史空间失败。历史预测不会作为当前物理碰撞显示。选中霸王龙可在 Scene 中查看青色出口路径和黄色连接点。

Unity 菜单：

- `Jurassic World/Acceptance/Verify T-Rex Body and Head Envelopes`
- `Jurassic World/Acceptance/Verify Unobstructed Roar to Follow`
- `Jurassic World/Acceptance/Verify Follow Exits and Step4`
- `Jurassic World/Acceptance/Measure Exit Planner Cost`
- `Jurassic World/Acceptance/Verify Animation Timing`

包络重建入口：`Jurassic World/Interaction/Rebuild T-Rex Body and Head Envelopes`，读取 Main 实际引用的互动配置。当前 Main 引用 `Assets/Scenes/TRexFrontTestAssets/SideInteractionProfile.asset`，包含 26 个动作；不能只检查基础的 22 动作配置。

验收在独立 Play 夹具中克隆地形、创建临时导航和障碍，不保存夹具到 Main。真实 Main 的互动、路线和同伴避让另行回归。

## 报告

- [测试汇总](ExitValidationSummary.md)
- [出口与 Step4 场景](ExitPlannerAcceptance.md)
- [无遮挡左右 Roar → Follow](OpenRoarFollow.md)
- [规划耗时与分配](ExitPlannerPerformance.md)
- [动画播放时长与支撑脚误差](AnimationTimingAfter.md)
- [身体与头部包络尺寸](BodyHeadEnvelopes.md)
- [包络筛选与硬／软障碍检查](BodyHeadAcceptance.md)

`ExitPlannerPerformance.md` 比较同一固定姿态、相同包络与导航条件下的旧式完整转身复核和新规划服务。它是算法微基准，不代表整帧 CPU 或 GPU 性能。单次不可拆分操作可能超出软预算；报告包含实际中位值、P95、最大值及超时次数。若 Unity 的线程分配计数器未通过显式分配探针，则标记不可用，不把零读数当作零分配。
