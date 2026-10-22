# 蜥结龙验收记录

2026-10-21，Unity 2022.3.62f3c1，Main 场景。

```text
PASS: Main; 75.0s simulation; 75.5s wall; 34.26m; turns=35.0; movingFrames=1897; frozenFrames=708; resumes=4; loops=32; legMotion=3628.8 degrees; walkSpeed=0.650m/s; animationRate=3.192x.
```

- 两次增量接入后仍只有一只蜥结龙，其他恐龙的位置、旋转、缩放以及现有场景根节点保留。
- 原生 Legacy Walk 长 5 秒，贴图及实际渲染可见性检查通过；无 Idle 和脚部锁定。
- 显示尺寸约 5 米，导航身体采样长度约 4.96 米，原生步速参考值约 0.204 米/秒，实际行走速度为 0.65 米/秒，动画倍率为 3.192 倍。
- 检查了 Main 中行走与停步截图，以及仅渲染模型的截图。
- 测试后关闭 Play，保存的场景不包含测试专用出生种子或禁用输入设置。

截图位于 Logs/sauropelta-model.png、Logs/sauropelta-walk.png、Logs/sauropelta-stop.png。
