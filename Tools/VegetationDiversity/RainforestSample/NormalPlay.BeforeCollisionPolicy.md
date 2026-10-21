> Historical result using the previous all-vertices/static-penetration assertions; not the current collision policy.

# Normal Play regression — tree sizing

2026-10-21：FAIL，停在出生种子检查，未运行后续十分钟漫游及 60/144 FPS 漫游。不能把历史 NormalPlay.mp4 当成本轮通过证据。

缩放后的 Main 在 seed=731729 时发现霸王龙网格与硬木重叠：根位置 (59.79, 0, 87.65)，网格点 (59.22, 2.52, 89.83)，木质截面中心 (59.66, 90.06)、半径 0.6647351。这在样板区 X[-25,25]、Z[-10,45] 之外。此前的出生种子检查均完成。

日志：Logs/tree-sizing-play.log。检查使用原有 ForestPlayAcceptance，包括全部网格顶点；未降低断言或关闭同伴。

修改前场景复核同样失败，但首次失败种子和位置不同：seed=731725，根位置 (11.22, 0, 37.30)，网格点 (8.51, 2.59, 41.45)，木质截面中心 (8.67, 41.77)。见 [TreeSizingBaselinePlay.md](TreeSizingBaselinePlay.md) 和 Logs/tree-sizing-baseline-play.log。因此全网格出生回归在调整前已存在失败；这不等同于证明修改后每个位置都安全。

本轮受控互动、静态布局与路线检查单独记录在 [TreeSizingVerification.md](TreeSizingVerification.md)。恐龙运动代码保持修改前状态。
