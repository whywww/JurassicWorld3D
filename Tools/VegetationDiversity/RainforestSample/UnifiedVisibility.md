# 霸王龙统一互动范围回归

PASS：发现与互动均为32米、±50°；±20°和±50°边界包含在内，范围不随阶段扩大。

PASS：10米Near、10～15米回差、>15米恢复条件；不可见玩家不能触发Near。

PASS：已登记岩石遮挡；未登记的草蕨碰撞体不影响视线（真实植被分类另见既有审计）。

PASS：30 FPS模拟，Intro：短暂丢失恢复、目标冻结；开场表演不因视线丢失中止。
PASS：30 FPS模拟，Intro：短暂丢失恢复、目标冻结；开场表演不因视线丢失中止。
PASS：30 FPS模拟，Follow，等待=False：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：30 FPS模拟，Follow，等待=True：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：30 FPS模拟，Near，等待=False：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：30 FPS模拟，Near，等待=True：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：60 FPS模拟，Intro：短暂丢失恢复、目标冻结；开场表演不因视线丢失中止。
PASS：60 FPS模拟，Intro：短暂丢失恢复、目标冻结；开场表演不因视线丢失中止。
PASS：60 FPS模拟，Follow，等待=False：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：60 FPS模拟，Follow，等待=True：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：60 FPS模拟，Near，等待=False：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：60 FPS模拟，Near，等待=True：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：144 FPS模拟，Intro：短暂丢失恢复、目标冻结；开场表演不因视线丢失中止。
PASS：144 FPS模拟，Intro：短暂丢失恢复、目标冻结；开场表演不因视线丢失中止。
PASS：144 FPS模拟，Follow，等待=False：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：144 FPS模拟，Follow，等待=True：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：144 FPS模拟，Near，等待=False：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：144 FPS模拟，Near，等待=True：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。
PASS：30 FPS，Follow，等待=False：中断/等待不重置跟随计时，累计30秒后结束。
PASS：30 FPS，Follow，等待=True：中断/等待不重置跟随计时，累计30秒后结束。
PASS：30 FPS，Near，等待=False：中断/等待不重置跟随计时，累计30秒后结束。
PASS：30 FPS，Near，等待=True：中断/等待不重置跟随计时，累计30秒后结束。
PASS：60 FPS，Follow，等待=False：中断/等待不重置跟随计时，累计30秒后结束。
PASS：60 FPS，Follow，等待=True：中断/等待不重置跟随计时，累计30秒后结束。
PASS：60 FPS，Near，等待=False：中断/等待不重置跟随计时，累计30秒后结束。
PASS：60 FPS，Near，等待=True：中断/等待不重置跟随计时，累计30秒后结束。
PASS：144 FPS，Follow，等待=False：中断/等待不重置跟随计时，累计30秒后结束。
PASS：144 FPS，Follow，等待=True：中断/等待不重置跟随计时，累计30秒后结束。
PASS：144 FPS，Near，等待=False：中断/等待不重置跟随计时，累计30秒后结束。
PASS：144 FPS，Near，等待=True：中断/等待不重置跟随计时，累计30秒后结束。
PASS：Intro、Ending、TurnBack、ReturnWalk 不计跟随超时。

计时测试直接调用生产可见更新函数；全流程运动、安全和路线使用既有Play回归单独验证。场景未保存。

