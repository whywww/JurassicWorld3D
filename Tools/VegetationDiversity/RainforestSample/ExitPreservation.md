# 本轮资源与场景核对

相对于包络修改前保存的 Main 基线，场景序列化对象数量一致（5297），仅 Interaction Body 组件的包络数据发生变化。树木、地形、大石、导航和其他场景对象未因本轮修改发生变化。烘焙采样引入的两项骨骼旋转覆盖已恢复。

24 个原始动画／导航资源逐一 SHA-256 对比一致。Main 实际引用 SideInteractionProfile 的 26 个动作：原始播放参数、根位移、脚步与玩家姿态采样均与修改前一致，新增 obstaclePoses 仅供身体／头部空间检测。

包络尺寸与形状参见 BodyHeadEnvelopes.md 和 BodyHeadEnvelopeComparison.png。尾巴与四肢不参与互动硬障碍拒绝；XZ 每侧余量 0.05 米。
