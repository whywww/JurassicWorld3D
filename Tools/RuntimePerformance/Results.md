# Main 场景运行时优化实测

2026-10-26。本轮已改善中档帧时间，但尚未达到稳定 30 FPS 的验收标准，也没有达到 60 FPS。以下 FPS 均为 `1000 / 帧时间中位数`，不代表最慢帧或开启垂直同步后的屏幕刷新率。

## 测量条件

- i5-11600KF、RTX 3080（10 GB）、约 16 GB RAM；Unity 2022.3.62f3c1、Direct3D 11、多线程渲染。
- Main，中档，主相机自动渲染至 1920×1080。Windows 独立程序为 Development Build，无 Deep Profiling，窗口 1280×720，世界相机输出仍是 1920×1080。
- 每视点预热 15 秒、采样 60 秒、三次重复。三个固定视点和同一条 60 秒路线；动物继续活动，因此接触数量、遮挡和行为并非逐帧相同。
- 采样期间关闭 VSync、解除目标帧率限制，结束后恢复。采集器不调用 `Camera.Render()`；截图读回及文件输出在计时区间之外。
- 编辑器数据来自独立测试副本的 batchmode Play，并非用户交互式 Game/Scene 窗口的通用 FPS 保证。独立程序使用可见窗口；隐藏窗口及 Player batchmode 曾跳过世界渲染，这些黑屏数据已作废。
- 下表采用三次中位数的中位数，以及三次 P95 的最大值。验收检查每次采样，中位数须 ≤33.3 ms、P95 须 ≤40 ms。

## Windows 独立程序

|视点|优化前中位 ms|优化后中位 ms|优化前 FPS|优化后 FPS|最差 P95 前→后 ms|第一阶段|
|---|---:|---:|---:|---:|---:|---|
|森林|52.79|34.33|18.9|29.1|98.02 → 87.84|未通过|
|溪流|67.42|50.32|14.8|19.9|106.67 → 104.08|未通过|
|恐龙区域|31.12|25.14|32.1|39.8|84.86 → 66.05|未通过|
|漫游路线|42.15|32.33|23.7|30.9|100.98 → 64.15|未通过|

原始数据：[优化前](E:/Unity/JurassicWorld/Logs/RuntimePerformance/baseline-player-visible/results.md)、[优化后](E:/Unity/JurassicWorld/Logs/RuntimePerformance/optimized-player-visible/results.md)、[CSV 对照汇总](E:/Unity/JurassicWorld/Logs/RuntimePerformance/player-comparison.md)。目录同时包含逐帧 CPU、GPU、Draw、Triangles、GC 等数据及机位记录。优化前渲染线程计数器不可用；优化后补充了 FrameTimingManager 的有效 CPU 渲染线程回退计时，不能据此计算该线程的前后收益。

“恐龙区域”为固定世界坐标；动物可以离开，不等价于持续占据画面的恐龙近景压力测试。

下列诊断值直接从 CSV 计算各次中位数，再取三次中位数；偶数样本采用中间两值平均，因此可能与原始报告的最近秩取值略有不同。Draw/Triangles 的采集范围限制见文末。

|Player 视点|CPU 主线程 ms 前→后|CPU 渲染线程 ms 前→后|GPU ms 前→后|Draw 前→后|Triangles 百万 前→后|GC bytes/帧 前→后|
|---|---:|---:|---:|---:|---:|---:|
|森林|50.65 → 30.96|N/A → 14.64|37.38 → 22.92|7514 → 4716|24.04 → 15.37|4156 → 2338|
|溪流|68.45 → 50.99|N/A → 26.29|46.64 → 35.06|11278 → 7676|38.22 → 28.83|8102 → 2359|
|恐龙区域|31.06 → 25.30|N/A → 8.18|26.14 → 18.68|4502 → 4451|71.54 → 72.87|3854 → 2504|
|路线|41.24 → 32.90|N/A → 13.75|31.61 → 23.46|6952 → 5005|31.74 → 24.73|7736 → 2569|

## 编辑器 Play

|视点|优化前中位 ms|优化后中位 ms|优化前 FPS|优化后 FPS|最差 P95 前→后 ms|第一阶段|
|---|---:|---:|---:|---:|---:|---|
|森林|82.62|40.63|12.1|24.6|129.61 → 111.93|未通过|
|溪流|109.57|50.37|9.1|19.9|186.17 → 100.97|未通过|
|恐龙区域|51.00|33.14|19.6|30.2|100.32 → 77.61|未通过|
|漫游路线|74.01|38.15|13.5|26.2|112.16 → 86.83|未通过|

首次编辑器采集的恐龙相机跟随初始随机出生位置，前后坐标不一致，已排除该项比较。表中该项来自同一固定坐标的新一轮 15/60/3 采样，其余三项保留完整原始采样。

数据合成说明：[优化前来源](E:/Unity/JurassicWorld/Logs/RuntimePerformance/baseline-editor-fixed/COMPOSITION.md)、[优化后来源](E:/Unity/JurassicWorld/Logs/RuntimePerformance/optimized-editor-fixed/COMPOSITION.md)。[有效 CSV 对照](E:/Unity/JurassicWorld/Logs/RuntimePerformance/editor-comparison.md)会校验固定机位坐标及主要构建参数，拒绝不一致的比较。

编辑器 GPU 和 CPU 渲染线程计数器不可用，未以 CPU 提交时间推算 GPU 时间。

## 保留的改动

- 信息卡复用主相机深度，保留同步测试及回退路径、植被遮挡、实际 LOD 与原选择规则。正常路径缓存目标、候选集合、缓冲区和命令，最多一个活动异步读回请求。独立程序诊断记录的额外探针场景重绘次数为 0。
- 植被接触缓存运动参数和静止根变换；静止网格节点跳过求解，标量弹簧减少运算和数组写入。慢帧限制子步数量，避免卡顿反过来增加计算量；30/60/144 FPS 的原有子步安排保留。
- 中档视觉接触预算为 55 米，已有形变增加 10 米恢复区；近处保持接触和回弹。刚性树干、导航、恐龙数量和交通行为未改动。
- 18 个兼容材质开启实例化；形变植被没有静态合批。植被恢复静止时恢复原属性块，避免残留交互属性阻止实例化。
- 只给明确标记、无既有 LOD 的小型地表植物增加远距剔除。中档阴影距离 65 → 50 米，LOD bias 1 → 0.85；近景植被密度与默认渲染分辨率保留。
- HUD 缓存显示文本、复用标记集合，保留动态目标和小地图刷新；为主要脚本子系统增加 Profiler 标记。

## 验证

当前工作区 firstpass、运行时和编辑器代码编译通过。接触数学 240,000 次对照通过（最大力偏差约 2.33×10⁻⁷）；标量弹簧 240,000 帧对照通过（最大位置偏差约 0.12 mm，位移上限保持）。

|检查|结果|记录|
|---|---|---|
|重复切换低/中/高、保存键、近远植被预算、已有 LOD|通过|[日志](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-budgets.log)|
|信息卡遮挡、透明/裁剪叶片、地形、实际 LOD、互相遮挡、翼龙、异步清理|68 项通过|[日志](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-visibility.log)|
|动画时序和脚步支撑、转向|通过|[日志](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-animation.log)|
|吼叫转跟随、跟随移动和支撑|通过|[日志](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-follow.log)|
|返回等待、转向、开场行走、同伴与拐角|通过|[日志](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-return.log)|
|Emotiv 键盘/命令模拟、MindWave 本地输入|通过；无物理设备/授权测试|[Emotiv](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-emotiv.log)、[MindWave](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-mindwave.log)|
|小地图、动态目标、坐标/方向、启停和场景重载|通过|[日志](E:/Unity/JurassicWorld/Logs/runtime-performance-regression-navigation.log)|
|完整植被接触/恢复与图形检查|功能通过；专项开销预算未通过|[记录](E:/Unity/JurassicWorld/Logs/RuntimePerformance/regressions/foliage-verification.txt)|
|当前完整工作区快照（包含同期脑控改动）|Unity 编译、16,274 项输入检查、Play 启动及真实相机绘制通过|[输入日志](E:/Unity/JurassicWorld/Logs/runtime-performance-current-input.log)、[短时启动检查](E:/Unity/JurassicWorld/Logs/RuntimePerformance/current-workspace-smoke/results.md)|
|当前完整工作区 Windows Development 构建及启动|构建与短时真实相机绘制通过；正式性能数据仍采用冻结快照|[构建日志](E:/Unity/JurassicWorld/Logs/runtime-performance-current-build.log)、[启动检查](E:/Unity/JurassicWorld/Logs/RuntimePerformance/current-player-smoke/results.md)|
|恐龙交通互斥|未全通过，原始 Agent 同样失败|[优化版结果](E:/Unity/JurassicWorld/Logs/runtime-performance-traffic-optimized-result.txt)、[原始 Agent 对照](E:/Unity/JurassicWorld/Logs/runtime-performance-traffic-baseline-result.txt)|

交通测试的物种优先级、迎面、交叉、超车和站立阻挡通过；随后在 `initial circle overlap` 触发“新增或加深身体交叠”断言，后续案例未执行。移除本轮唯一的 Agent 计时包装后复测，结果文件逐字节一致，因此列为既有问题，未在本轮改写避让或碰撞规则。

植被专项在相同路径/相机下，关闭交互、准备后静止、接触三种状态的 CPU 渲染提交加交互平均耗时分别为 10.825、11.970、13.811 ms：额外开销约 10.6% / 27.6%，超过该旧专项的 5% / 10% 目标。这是 CPU 局部计时，不能替代完整帧时间或 GPU 验收。

正式前后基准每轮连续运行约 15 分钟，其中动态路线累计采样 3 分钟；未覆盖小时级耐久测试。当前工作区的编辑器与独立程序启动检查均为 3 秒预热、10 秒采样、1 次，仅验证整合运行，不作为正式 FPS 成果。当前整合后的 Development 程序位于 [PerformanceCurrent/JurassicWorld.exe](E:/Unity/JurassicWorld/Builds/PerformanceCurrent/JurassicWorld.exe)。

## 画面对照

|固定机位|优化前|优化后|
|---|---|---|
|森林|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/baseline-player-visible/normal-forest-1.png)|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/optimized-player-visible/normal-forest-1.png)|
|溪流|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/baseline-player-visible/normal-creek-1.png)|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/optimized-player-visible/normal-creek-1.png)|
|恐龙区域|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/baseline-player-visible/normal-dinosaur-1.png)|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/optimized-player-visible/normal-dinosaur-1.png)|
|路线终点附近|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/baseline-player-visible/normal-route-1.png)|[PNG](E:/Unity/JurassicWorld/Logs/RuntimePerformance/optimized-player-visible/normal-route-1.png)|

近景密度、树冠轮廓和雾效由固定视点截图核对；风、阴影中的动物及接触状态仍随运行变化，截图不是逐像素确定性回放。材质改动清单见 [18 个实例化材质](E:/Unity/JurassicWorld/Logs/RuntimePerformance/instancing-materials.md)。

## 剩余瓶颈与测量限制

- 优化后独立程序溪流三次 GPU 中位数约 26.7–35.5 ms，森林约 20.3–25.8 ms；渲染仍占据明显预算。CPU 和 GPU 存在重叠，不能相加。
- 接触密集时植被模拟仍会上升，恐龙导航/行为也有尖峰；未根据单个慢帧改变近处动画、避让或碰撞安全。
- 原始 Draw/Triangles 计数可能涵盖深度、阴影、辅助相机或最后一次提交，不能当成单一主画面的几何量，更不能直接换算 FPS。
- 逐项关闭的短诊断用于定位线索，正式对照只证明本轮组合改动的整体收益；未声称每一项资产调整都有独立量化收益。
- Frame Debugger 在 batchmode 及可见 Game view 两种模式中都未返回帧事件，未得到合批中断原因的证据。见 [记录](E:/Unity/JurassicWorld/Logs/RuntimePerformance/frame-debugger.md)及[可见窗口日志](E:/Unity/JurassicWorld/Logs/runtime-performance-current-frame-debugger.log)。物理脑控设备/授权未实机验证。

复现命令与边界见 [README](E:/Unity/JurassicWorld/Tools/RuntimePerformance/README.md)。原始测试和构建位于 `Logs/RuntimePerformance` 与 `Builds/Performance*`，使用隔离快照，未覆盖工作区同期的脑控改动。
