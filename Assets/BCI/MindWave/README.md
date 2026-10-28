# MindWave Mobile 2：可拆卸输入模块

适用 Unity 2022.3、Windows Editor / Windows x64 Player。设备现在由 Main Camera 的三个组件勾选框（EMOTIV / MindWave / BrainLink）互斥选择；主场景默认选中 MindWave Connector。全部取消勾选时仅用键盘。

## 开始测试

1. 在项目根目录打开 PowerShell，启动模拟器：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Assets\BCI\MindWave\Simulator\Start-MindWaveSimulator.ps1" -Attention 70
   ```

2. 打开主场景，选中 Main Camera，勾选 **Mind Wave Player Input**，另外两个设备自动取消。在组件 Inspector 中将 **Backend** 设为 `TcpSimulator`，保存场景后进入 Play。旧设置菜单现在只负责定位相机。
3. 不按方向键时，attention=70 的目标速度为当前满速的 0.5 倍；主场景为 2 m/s。
4. 模拟器终端输入命令并回车：`a 100` 满速、`a 40` 停止、`s 200` 信号差、`s 0` 恢复、`pause` 停止发包、`resume` 恢复、`drop` 断开、`quit` 退出。`auto` / `manual` 切换自动序列。
5. F9 显示或隐藏诊断面板。`forward target` 是归一化目标，不是瞬时实际速度；速度仍经过原移动控制器的平滑加减速。

自动序列可用 `-Auto` 启动：每段 4 秒，依次是 attention 39、40、70、100、0、信号差、恢复、暂停发送、断开重连，然后循环。`drop` 与自动断开仅适用于 TCP；串口链路可用 pause / resume 测试断流。`-DurationSeconds 60` 限制自动运行时间。支持 Windows PowerShell 5.1 和 PowerShell 7。

## Windows 成品

设置保存在场景的 MindWave 组件中，随构建打包；停止 Play 后修改并保存，重新构建生效。Play 期间设备选择与参数只读。

选中的 MindWave 组件可使用以下启动参数覆盖后端和端口：

```powershell
.\JurassicWorld.exe -mindwaveSource TcpSimulator -mindwavePort 13855
.\JurassicWorld.exe -mindwaveSource Connector
```

启动参数不选择设备。`Original` 的停用功能由取消组件勾选取代；不要用该旧参数启动新版本。

`TcpSimulator` 默认 127.0.0.1:13855，`Connector` 默认 127.0.0.1:13854。它们走完全相同的 TCP 接收、协议解析和移动映射。旧 Resources/MindWaveSettings.json 仅作为旧版 Load API 的兼容资源，新组件不从它读取配置。

## 真实设备链路

`MindWave Mobile 2 → 蓝牙 COM → 官方 ThinkGear Connector → TCP/JSON → Unity → 原玩家移动`

从项目根目录运行以下命令，将官方 Connector 及依赖提取至系统临时目录，不会启动程序、安装驱动或导入 SDK DLL 到 Unity：

```powershell
.\Assets\BCI\MindWave\Simulator\Extract-Connector.ps1
```

若 SDK ZIP 不在项目根目录，用 `-SdkZip '完整路径'`。按脚本输出的位置手动启动 Connector。真实设备购买后，先蓝牙配对并确认 COM，再在 Connector 中确认收到数据，在 Main Camera 勾选 MindWave，Backend 选择 `Connector`。

官方旧版 TGC 示例直接配置 JSON，无须等待授权回执，故默认关闭 **Require TGSP authorization**。若服务器要求授权，开启此选项：先发送 appName / 40 位十六进制 appKey，等 `isAuthorized:true` 后再配置 JSON。appKey 是应用标识，不是 EMOTIV 凭据；拒绝或等待授权期间不移动。

## 经过官方 Connector 的无设备模拟

2026-10-28 已在本机用 HHD 的 COM2 ↔ COM3 和 SDK 自带官方 Connector 验证到 TCP/JSON 输出；具体证据见 `Validation.md`：

`二进制模拟器 → 虚拟 COM 对 → 官方 Connector → TCP/JSON → Unity`

1. 当前主机为 Windows 11 x64（build 26200）。从 [HHD Software 官方虚拟串口站点](https://freevirtualserialports.com/) 安装工具后，创建一对空闲端口，例如本机已验证的 COM2 ↔ COM3。免费版有许可和功能限制，安装前查看官网条款。无需关闭驱动签名验证。
2. 启动二进制模拟器，独占 COM2：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Assets\BCI\MindWave\Simulator\Start-MindWaveSimulator.ps1" -Mode Serial -SerialPort COM2 -Attention 70
   ```

3. 打开官方 Connector，将 **Starting port** 指向 COM3，必要时点击“重试耳机连接”。模拟器为 57600 baud、8N1，发送 512 Hz 的合成 raw EEG 包和 1 Hz 的汇总包（信号质量、attention、meditation、八个频段功率），均带 `AA AA` 同步头、长度和反码校验和。初次识别可等待约 20 秒。
4. 在 Main Camera 勾选 MindWave，Backend 选择 `Connector`，观察 attention=70 和 forward target=0.50，再测试信号差、暂停与恢复。
5. 若 Connector 不识别该虚拟端口，保存 Connector 日志、COM 号和 Unity 诊断信息，将结果记为未通过，不添加猜测性的私有握手。可继续用 TCP 模拟器验证 Unity。

模拟器没有复现蓝牙配对和设备发现；即使该链路通过，也不代表真实蓝牙和佩戴质量通过。真实设备只能被一个设备/应用直接占用；使用 Connector 时关闭直接占用耳机 COM 的官方 Tutorial 等工具。

SDK 3.2 的 .NET 接收器需先累计 768 个包识别 TGAM。旧模拟器仅以 1 Hz 发送 attention 包，会在完成识别前被 Connector 判为超时；更新脚本后必须退出旧模拟器进程，再重新启动。raw 数据和频段功率均为合成测试值，不代表真人 EEG，也不用于计算这里手动设置的 attention。此要求属于该版本官方 Connector 的设备识别流程，Unity 仍只使用 1 Hz 的 attention 和信号质量。

## 输入和有效性规则

- `Vector2(0, Clamp01((attention - threshold)/(100 - threshold)))`，threshold 默认 40，允许 0–99。
- attention 必须为 1–100 整数；0、越界值、未知或非零 poorSignal、断线、授权拒绝均不能移动。
- attention 和信号质量分别计时，默认 3 秒过期。raw EEG、blink、其他 JSON 包不会续期。信号差恢复后必须收到新的有效 attention。
- 缺少字段不等于字段为零。TCP 支持 CR 分隔、拆包、粘包、空行及 CRLF；非法帧跳过，超过 64 KiB 的帧丢弃并在下一个 CR 恢复。
- 任意 WASD / 方向键优先；松开后恢复当前有效脑控。鼠标右键视角、地形高度、探索边界与原平滑移动保持原逻辑。
- MindWave 选中但连接不可用时输出零，不转到 EMOTIV。配置非法时显示错误并禁止脑控，键盘仍可用。

## 模块边界和删除

整个设备模块都在 `Assets/BCI/MindWave/`，包含配置、模拟器、测试和文档。相机上只有显式 MindWave 组件引用，无 SDK 原生依赖，无额外 Package 依赖。

主项目仅保留 `Assets/PlayerInput/` 的设备无关注册接口，以及 SmoothExplorerCamera / EmotivPlayerInput 中的通用输入挂钩。运行程序集不依赖项目的玩家类或 EMOTIV。

停止 Play、关闭独立程序，先从 Main Camera 移除 Mind Wave Player Input 组件，然后在 Unity Project 窗口删除 `Assets/BCI/MindWave/`（Unity 自动删除对应 meta）。可重新勾选 EMOTIV 或 BrainLink，全部关闭时仅用键盘。无需删除通用接口或修改 Build Settings。曾手动安装的虚拟串口工具独立于项目，按厂商方式另行卸载。

## 自动验收

- 菜单 **Jurassic World > BCI > Tests > MindWave Acceptance**：解析、阈值、信号、超时、注册独占、授权、真实回环 TCP、断开重连和重复 Dispose。在非 Play 状态执行。
- Batch 入口：`JurassicWorld.BCI.MindWave.Tests.MindWaveAcceptance.Run`。
- 原输入 / 地形 / 边界回归：`JurassicWorld.Editor.EmotivInputAcceptance.RunPlayerMovementRegression`。
- 主场景 Windows 构建入口：`JurassicWorld.BCI.MindWave.Tests.MindWaveAcceptance.BuildMain`，默认输出 `Builds/MindWaveValidation/JurassicWorld.exe`。
- 真实主场景移动烟测：独立模拟器 `-Auto`，Editor 用 `PlayMain` 入口，或运行 Windows 成品，加 `-mindwaveSource TcpSimulator -mindwaveSmoke -mindwaveSmokeReport '报告完整路径'`。测试观察半速/满速输入、实际位移、减速停止和 EMOTIV 未启动，最长 45 秒；成功/失败后自动退出。普通运行不激活测试。
- 连续 Play 生命周期：隔离工程 Batch 入口 `JurassicWorld.BCI.MindWave.Tests.MindWaveAcceptance.RunPlayLifecycle`，主场景选中 MindWave、后端选 `TcpSimulator`；检查关闭 Domain Reload 后两次 Play 的注册与 TCP 线程释放。
- 删除验收：从项目根运行 `Assets/BCI/MindWave/Tests/Test-Removal.ps1`。脚本创建不含模块的完整隔离副本，运行原输入回归并构建 Windows；不会删除或移动原项目。副本与构建位于忽略的 Builds 目录，可能占用数 GB。
- SDK 文件模式探测：64 位 Python 运行 `Tests/Probe-Sdk.py`。测试结果：官方 DLL 成功从二进制文件读取 attention=70；文本 hex 和原 streamLog 格式不能直接作为同样的二进制输入。此功能不作为 Unity 运行后端。
- 官方 .NET 串口帧回归：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File "Assets/BCI/MindWave/Tests/Test-SerialFrames.ps1" -ConnectorDirectory "官方 Connector 所在目录"`。自动使用 32 位 PowerShell，验证生产数据帧及校验和、768 包识别和 attention/信号质量解析，不打开 COM 端口。

验收结果见 `Validation.md`。软件模拟不能验证真实 attention 控制体验、蓝牙可靠性或佩戴信号质量。

## 官方参考

- [Windows Developer Tools 3.2](https://store.neurosky.com/products/pc-developer-tools)
- [Connector 开发指南](https://developer.neurosky.com/docs/doku.php?id=thinkgear_connector_development_guide)
- [ThinkGear Socket Protocol](https://developer.neurosky.com/docs/doku.php?id=thinkgear_socket_protocol)
- [attention 和信号质量说明](https://developer.neurosky.com/docs/doku.php?id=thinkgear.net_sdk_dev_guide_and_api_reference)
- [MindWave Mobile 2 连接说明](https://support.neurosky.com/kb/mindwave-mobile-2/cant-get-connection-with-mindwave-mobile-2)

本地 SDK 的对应 PDF 与头文件为版本依据。没有把旧 Unity 文档的 Unity Pro 限制套用到当前 Unity 2022 项目。
