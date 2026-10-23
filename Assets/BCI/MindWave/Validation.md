# 验收记录

日期：2026-10-22（Asia/Hong_Kong）。Unity 2022.3.62f3c1，Windows 11 x64 build 26200。

为避免干扰已打开的项目，Unity 自动验收在完整隔离副本中执行。主场景、场景构建器和原项目资源未修改。

| 检查 | 结果 | 覆盖范围 |
|---|---|---|
| MindWave Acceptance | PASS，54 项 | 0/39/40/70/100、超时、信号质量及过期恢复、拆包/粘包/坏帧/超长帧、输入注册、授权、真实回环 TCP、断线重连、Dispose |
| 原玩家移动回归 | PASS，39,049 项 | EMOTIV 映射与事件、键盘优先、地形视高、探索边界和穿树逻辑 |
| 连续 Play 生命周期 | PASS | 关闭 Domain Reload 后连续两轮 Play，注册和 TCP 线程均释放 |
| 主场景 Editor Play | PASS | 独立 PowerShell 自动模拟器 → 生产接收器 → 实际玩家位移；半速/满速、减速停止、EMOTIV 未启动 |
| 主场景 Windows x64 构建 | PASS | Development / Mono，默认仍为 Original |
| Windows 独立程序 | PASS | 外部模拟器驱动实际位移、半速/满速输入、减速停止、EMOTIV 未启动，退出码 0 |
| 模拟器 PowerShell 兼容性 | PASS | Windows PowerShell 5.1 与 PowerShell 7 均完成编译、监听和定时退出；成品联测使用 PowerShell 7 |
| 官方 SDK 文件模式 | PASS（二进制） | `TG_STREAM_FILE_PACKETS=2`，TG_Connect=0、TG_ReadPackets=1、attentionUpdated=1、attention=70；hex 文本和 streamLog 文本不能直接用同一方式读取 |
| 官方 Connector 本机接口 | PASS（仅接口） | SDK 内的 Connector 可在 Windows 11 启动，接受 TCP 13854 / JSON 配置，返回 `{"poorSignalLevel":200,"status":"scanning"}` |
| 删除模块后的原输入与构建 | PASS | 不含 Assets/BCI/MindWave 的完整隔离副本：39,049 项原输入回归通过，主场景打开与 Windows 构建通过 |
| 虚拟 COM → Connector 数据链路 | 未验证 | 本机只有 COM1，未安装虚拟串口；手动安装步骤与二进制发送器已提供 |
| 真实 MindWave Mobile 2 | 未验证 | 尚无设备；蓝牙、佩戴质量、实际 attention 控制体验待验收 |

本地复核位置（忽略的构建和日志，不随源码提交）：

- `Logs/MindWaveFinalRun.log`（最终 54 项；首轮为 `MindWaveAcceptance.log`）
- `Logs/MindWavePlayerRegression.log`
- `Logs/MindWaveRunPlayLifecycle.log`
- `Logs/MindWaveEditorSmoke.txt`
- `Logs/MindWaveFinalBuildMain.log`（最终构建；首轮为 `MindWaveBuild.log`）
- `Logs/MindWaveStandaloneSmoke.txt`
- `Logs/MindWaveConnectorProbe.txt`
- Windows 程序：`Builds/MindWaveValidation/JurassicWorld.exe`
- 带模块的隔离工程：`Builds/MindWaveValidationProject`
- 删除模块验证副本与日志：`Builds/MindWaveRemoval-213a44c2`（`RunPlayerMovementRegression.log`、`Run.log`）

当前工作区配置保持 `Original`。这些结果证明软件链路可工作，不代表真实蓝牙或真人 attention 控制效果已验证。
