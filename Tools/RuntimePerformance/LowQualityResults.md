# 低画质优化：保留植被优先

2026-10-28。按用户要求停止继续优化并收尾。**渲染优化已实现；保留全部植被的正式验收未达到稳定 60 FPS。** 当前默认 Low 使用完整渲染优化（Materials），软植被削减为 0%。不把短测或中位数超过 60 FPS 当成通过。

## 已实现的行为

- `-jwPerfQuality Low|Medium|High` 选择测试档位，命令行默认仍为 Medium；测试不写入保存的画质偏好。
- Low 清除接触形变，停止接触求解及恐龙用于植物接触的骨骼采样；保留原有普通风动。中、高档恢复交互并重置采样历史；手动禁用交互的状态保留。
- Low 关闭溪流平面反射。按原河道曲线烘焙的静态条带保留流动法线、水色、环境反射和岸边透明过渡；停止对应 Crest 组件与模拟。中、高档恢复原水材质、组件状态和 Crest。
- Low 使用共享植被材质变体；清理转换树木上妨碍实例化的非实例属性，保留实例颜色。简化叶片/树皮光照，保留纹理、裁剪阈值和原树木风动。相机调整不透明物体排序以减少提交；切回中、高档恢复原排序、材质和属性块。
- 保留信息卡所需主相机深度，未增加第二次世界相机渲染。导航缩略图相机保持快照方式。
- 不修改 Main 场景、恐龙数量/生产行为/脑控/导航/树干几何，不新增可见距离、密度、雾或 LOD 削减；原 Low 已有 Terrain/LOD 参数保持原样。
- 分阶段入口 `Baseline → Contact → Reflection → Water → Batching → Materials` 和可见编辑器菜单支持复测。默认 Low 保留所有植被，累计削减仅为显式测试选项。

## 正式测试：全部植被保留

本机 Unity 2022.3.62f3c1 编辑器 Play，i5-11600KF / RTX 3080，D3D11。桌面解锁、可见最大化 Game View、实际渲染 1920×1080；UI 显示比例不改变渲染分辨率。Scene View 关闭，Deep Profile 和额外 Profiler 录制关闭。动物保持活动，初始随机种子 73021。每个视点预热 15 秒、采样 60 秒、重复三次。

下表中位数为三次中位数的中位数，P95 为三次中的最差值；验收逐次判断中位数和 P95 均 ≤16.7 ms。FPS = 1000 / 汇总中位数，不代表最低帧率。

|视点|原 Low 中位数 ms|优化后中位数 ms|原/优化后 FPS|优化后最差 P95 ms|60 FPS 验收|
|---|---:|---:|---:|---:|---|
|森林|30.51|11.25|32.8 / 88.9|33.48|FAIL|
|溪流|45.68|28.46|21.9 / 35.1|57.95|FAIL|
|活动恐龙近景|26.99|15.51|37.1 / 64.5|51.23|FAIL|
|漫游路线|30.63|10.68|32.6 / 93.7|28.82|FAIL|

原 Low 与优化后均有 12 个完整正式样本和成功完成标记，且源码/场景哈希在采样期间未变化。活动恐龙近景三次均有主相机提交；跟随动物位置和朝向，动态视野不完全相同，不能把近景变化归因于单项优化。固定森林/溪流机位一致。

- [严格汇总](../../Logs/RuntimePerformance/low-60fps-full-population.md)
- [原 Low 数据](../../Logs/RuntimePerformance/low-baseline-acceptance-20261027-102230/results.md)
- [优化后数据](../../Logs/RuntimePerformance/low-materials-acceptance-20261027-103838/results.md)
- [原溪流截图](../../Logs/RuntimePerformance/low-baseline-acceptance-20261027-102230/normal-creek-1.png)、[优化后溪流截图](../../Logs/RuntimePerformance/low-materials-acceptance-20261027-103838/normal-creek-1.png)

优化后保留 762 个交互植物、775 个树干障碍和 6 个地面恐龙；新增一个低画质水面 Renderer。植被 Renderer 的启用状态、距离和数量不变。飞行动物未因本次优化削减。

## 软植被削减测试与边界

安全清单以原始 1757 株独立软植被为基准，不含树干、带碰撞/导航障碍的植物、恐龙和原生 Terrain 细节草层。每个条目为整株，固定排序并先筛外围、再筛远离既有漫游通道者。不是每帧按相机随机剔除，也不是全场植被总量的 30%。

|累计削减|隐藏整株数|保留清单数量|溪流短测中位数 ms|短测 P95 ms|
|---|---:|---:|---:|---:|
|10%|175|1582|21.77|65.98|
|20%|351|1406|20.66|80.28|
|30%|527|1230|21.46|60.63|

这些是 3 秒预热 / 10 秒采样 / 单次诊断，均未达标，不能替代正式验收；早期诊断布局和动态动物负载存在变化，不能用于精确计算单项收益。[逐阶段原始汇总 CSV](LowQualityDiagnostics.csv) 保留了条件与限制。

30% 上限的 15/60/3 长测于 2026-10-28 启动，在用户要求停止优化并收尾后中止，未完成恐龙近景与漫游的三次采样。收尾时窗口已最小化，持续可见性无法保证；该次数据标为 [INVALID](../../Logs/RuntimePerformance/low-materials-thin30-acceptance-20261028/INVALID.md)，不作正式验收或削减收益证据。只有全植被版本已有完整正式前后对照。当前默认仍保留全部植被；已停止继续优化，不超过 30% 削减，不降低分辨率或恐龙数量。

## 剩余瓶颈和计数器限制

全量植被优化后的溪流，三次中位数为 27.43～28.78 ms。GPU 中位数约 15.43～16.02 ms，CPU 主线程约 27.82～28.69 ms，渲染线程约 12.49～13.27 ms；仍有约 3537～3723 次绘制、3482～3675 个批次、2825～3016 次 SetPass 和约 1184～1188 万三角形。植物接触求解为 0，AI 中位数约 0.89～1.00 ms、足部处理约 0.09 ms。剩余问题集中在渲染提交、绘制成本及长帧，不能通过继续删除少量外围软植物可靠解决。

原生计数器确认存在实例化，但约 2800～2900 个实例化批次说明提交仍多。Frame Debugger 在可见编辑器中返回空事件，明确记为不可用，未推断具体批次中断原因。Terrain 实际为 512 分辨率 / 8×8 分块；16/32 的内存副本保留密度图总量，但没有作为生产改动保留。直接关闭原生 Terrain 植被的诊断仍约 28 ms，未证明重建 Terrain 能达标。密度设 0 的首次隔离未有效移除绘制，不作为其成本证据。

计数器可能延迟一帧，CPU/GPU 时间不能相加；EditorLoop / EditorApplication.update 不可用，不记作零，也不把剩余耗时全部归咎于编辑器。主相机深度仍供信息卡读取，正式采样没有额外 `Camera.Render()`。所有截图写入在计时区间外。

## 功能回归

|检查|结果|证据|
|---|---|---|
|运行时与编辑器代码|编译通过|`Logs/low-quality-runtime-compile.log`、`Logs/low-quality-editor-compile.log`|
|反复切换 Low/Medium/High、原材质/水体/相机恢复、完整植被保留、接触恢复、固定 10/20/30 筛选、树干保护|240 条通过记录及总体通过标记|`Logs/RuntimePerformance/low-quality-regression-materials-Budgets.log`|
|优化后 Low 导航、HUD、低画质场景重载、重载后植物/树干/恐龙数量一致|通过|`Logs/RuntimePerformance/low-quality-regression-materials-NavigationLow.log`|
|信息卡深度、裁剪叶片/地形/恐龙遮挡、LOD、异步资源生命周期|68 项通过，本次进程正常退出|`Logs/RuntimePerformance/low-quality-regression-materials-Visibility.log`|
|Emotiv 软件回归|16,274 项通过|`Logs/RuntimePerformance/low-quality-regression-Emotiv.log`|
|MindWave 软件回归|54 项通过|`Logs/RuntimePerformance/low-quality-regression-MindWave.log`|
|严格性能报告回归|7 项通过，拒绝超预算、短测、batchmode、无近景恐龙、缺失重复、未完成与采样不足|`Logs/low-quality-comparison-tests.log`|

脑控结果限软件夹具，未测试实体头戴设备、蓝牙与授权。功能回归不作为 FPS 证明。运行方法、菜单及参数见 [README](README.md)。

收尾状态：已停止本轮测试进程，保留当前代码与原始数据；清除了 Unity 自动升级 QualitySettings 序列化产生的无关差异。未提交 Git。
