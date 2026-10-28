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
| 虚拟 COM → Connector 数据链路 | PASS（2026-10-28，到 TCP/JSON 输出） | HHD COM2 ↔ COM3，SDK 自带官方 Connector 输出 attention=70、poorSignalLevel=0；详见追加联调记录 |
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

初次验收配置为 `Original`；2026-10-28 用户串口联调时已选择 `Connector`。这些结果证明软件链路可工作，不代表真实蓝牙或真人 attention 控制效果已验证。

## 2026-10-28 官方 Connector 串口联调

- 实际使用的官方程序：`E:/Windows Developer Tools 3.2/ThinkGear_Connector/ThinkGear_Connector/ThinkGear Connector.exe`，不是 Builds 中的副本。
- 直接读取 COM3 收到旧模拟器数据 `AA AA 04 02 00 04 46 B3`，证明 HHD COM2 ↔ COM3 转发正常。
- SDK 3.2 的托管接收器需要先累计 768 个包识别 TGAM；旧模拟器 1 Hz 极简输出在识别前触发 Connector 超时，不能据原生 DLL 能读文件推断托管 Connector 能立即识别。
- 修正 Serial 模式：合成 raw EEG 包 512 Hz，attention/信号质量/meditation/频段功率汇总包 1 Hz。TCP 模式及 Unity 生产代码未改动。
- `Test-SerialFrames.ps1`：PASS 33 项，使用生产帧构建器和用户同一版本官方 .NET DLL，验证同步头、长度、校验和、识别后 attention=0/39/40/70/100 和信号质量=0/200。
- 用户重启模拟器后，从官方 Connector 的 TCP 13854 实际收到：`{"eSense":{"attention":70,"meditation":50},"eegPower":{"delta":1000,"theta":1100,"lowAlpha":1200,"highAlpha":1300,"lowBeta":1400,"highBeta":1500,"lowGamma":1600,"highGamma":1700},"poorSignalLevel":0}`。
- 本地证据：`Logs/MindWaveSerialProbe.txt`、`Logs/MindWaveLiveConnector.jsonl`；更新间隔测量保存于 `Logs/MindWaveLiveConnectorCadence.txt`。
- 连续采集窗口中收到 34 个包含 attention=70 的 JSON 帧，后续帧间隔约 0.25–0.29 秒，未触及 Unity 3 秒过期门限。此为 Connector JSON 输出节奏，模拟器的 attention 汇总仍为 1 Hz，不表示 eSense 算法以 4 Hz 更新。
- 本次确认的是官方串口到 TCP 输出；真人设备仍未验证。Unity 既有 TCP 接收与移动验收见上表，本次没有重新运行主场景自动移动测试。
