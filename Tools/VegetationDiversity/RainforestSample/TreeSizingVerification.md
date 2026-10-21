# 样板树木尺寸协调与验证

后续更新：本文记录的路线转身卡住问题已修复，三组路线组合全部通过；具体规则及验证见 [RouteTurnRepair.md](RouteTurnRepair.md)。下方保留树木尺寸调整当时的检查记录。

2026-10-21。本轮只调整新增样板植被及其生成入口，保留已有恐龙运动代码。Main 保存最终样板；不新增植物模型、材质或动画资源。

## 尺寸与布局

- 取消统一 15～21 米高度。tree01/02/03/04 的基准为 10/9/8/6 米，Pachira_B 为 6 米，与外围生成器一致；等比例乘固定种子的 0.82～1.14 倍变化。
- 新增乔木仍为 29 棵，实际高度 4.93～11.36 米；27 棵保留原位置及树种，2 棵因硬木冲突使用安全候选。逐棵记录见 [TreeSizing.md](TreeSizing.md)。
- 保留原样板林隙中心 (-14,5)、(14,27) 及弯曲连接通道。原设计分区记录为：总面积 2750m²、林隙 1176m²、通道及路线 989m²、可种植面积 585m²；29 棵对应外围参照密度的 65.1%。这些是保留的既有设计分区面积，不是用当前包络重新分区后的最小净空面积。
- 当前项目动画包络比原建样板时更细，直接重新分区会得到 980m² 可种植面积并增种至 49 棵。本轮没有采用该扩种方案，也没有扩大排除区来提高密度比例。
- `Assets/Editor/RainforestSamplePlanting.json` 保存原种植点、树种、朝向、尺寸分位及原布局。重建优先使用这些点，冲突才使用安全候选。修改前完整场景备份为 `Main_BeforeTreeSizing.unity`。

## 三机位对照

同一 Main 相机参数、1280×720、编辑状态渲染，机位坐标与现有样板性能检查一致。高度、树干和树冠宽度同时等比例调整。

|机位|调整前|调整后|
|---|---|---|
|入口|[图片](size-before-0.png)|[图片](size-after-0.png)|
|林内|[图片](size-before-1.png)|[图片](size-after-1.png)|
|侧面|[图片](size-before-2.png)|[图片](size-after-2.png)|

已查看三张调整后图片，入口及林内高树突兀的情况明显减轻；侧面机位依然有近景叶片遮挡。未重新测量 CPU/GPU，本目录 Performance.md 与已有录像不能作为本次性能或普通 Play 通过的证据。

## 静态与运动检查

构建入口依次恢复枝叶转换、补植、重新生成硬木截面及导航、重新准备枝叶交互。`Build and Verify` 包含重复生成签名一致、移除后恢复、Bake 接入，以及外围对象变换和 Terrain 树、地形高度、涂层不变的检查。最终构建日志：`Logs/tree-sizing-build-final.log`。

现有 30/60/144 模拟 FPS 受控互动共 24 组检查通过，记录见 [Acceptance.md](Acceptance.md) 及 `Logs/tree-sizing-interactions.log`。它们沿用当前项目规则：表演允许穿过静态物体，实际行走检查碰撞；不等同于完整身体动画全程不穿树的证明。

首次尺寸重建的原路线回归前两组完成：霸王龙 route 0/1 分别约 1175.8/1060.5 米，伶盗龙 route 4/2 分别约 1477.9/1408.4 米。第三组 FAIL：霸王龙 route 3 在 (-43.24,51.89) 的 90° 左转被 `Route corridor` 卡住 120 秒，`trunk=False`、`peer=none`。修改前场景同一路段也 FAIL：(-44.11,50.38)，同一左转动作和 `Route corridor` 原因。两个位置都在样板区外。日志分别为 `Logs/tree-sizing-routes.log`、`Logs/tree-sizing-baseline-routes.log`；未放宽测试断言或修改运动代码。最终重建进一步合并同身体分块的动画过渡包络，重新筛选了两个替换候选，并复跑互动检查；未再次重跑已经发现既有失败的整套路线。最终构建中六条路线至林隙的导航连通检查通过。

首次尺寸重建的全场景普通 Play 出生检查 FAIL；修改前场景复核也 FAIL，首次失败种子和位置不同。该段为历史严格全网格判定结果；当前碰撞策略将可见网格重叠作为诊断，新的回归单独记录在 [NormalPlay.md](NormalPlay.md)。历史详情见 [NormalPlay.BeforeCollisionPolicy.md](NormalPlay.BeforeCollisionPolicy.md) 与 [修改前复核](TreeSizingBaselinePlay.md)。最终重建的出生位置及其离开空间继续由构建安全筛选保护，未重新运行因既有问题而中止的普通 Play 全套检查。本轮不能声明全场景出生、十分钟漫游及全部路线均通过。

## 重建与撤销

保存场景并退出 Play，运行 `Jurassic World > Environment > Rainforest Sample > Build and Verify` 可重复构建、检查并保存。`Remove Sample` 仅移除该样板并重建导航与枝叶交互；种植记录保留供再次生成使用。完整场景 Bake 使用同一尺寸规则及种植记录。
