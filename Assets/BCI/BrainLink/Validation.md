# BrainLink 接入验证记录

日期：2026-10-28。项目：JurassicWorld，Unity 2022.3.62f3c1，Windows x64。

## 实际结果

| 验证层 | 结果 | 证据（项目 Logs 目录） |
|---|---|---|
| 环境准备 | PASS；独立 Python 3.11.18 x64、pyserial 3.5；第二次运行准备脚本成功 | `Builds/BrainLinkTools/python-env` |
| 官方 SDK 与 Python 桥接 | PASS；6 组测试，含 35 个拆包位置、逐字节、连续包、坏校验和、raw 不触发 EEG、TCP 命令与重连 | `BrainLink-PythonTests.log` |
| Unity 协议、网络与 Inspector 基础 | PASS；142 项检查 | `BrainLink-Acceptance.log` |
| 原生 Inspector 属性事件 | PASS；修改原生 m_Enabled 自动关闭其他来源，Undo 同时恢复原选择；保存场景未被测试修改 | `BrainLink-InspectorEvents.log` |
| Play 选择锁定与清理 | PASS；实际 Inspector 原生组件标题在 Play 被禁用；禁用 Domain Reload 连续两次 Play，退出后 provider 与线程释放 | `BrainLink-Lifecycle.log` |
| 意外多来源启用 | PASS；同时启用 BrainLink 与 EMOTIV 的测试对象，两者都没有启动连接 | `BrainLink-Lifecycle.log`；其中 BCI 配置冲突 Error 是有意触发的测试结果 |
| Editor 主场景移动 | PASS；半速实测 2.00 m/s，满速 4.00 m/s；平滑停止、信号差恢复、3 秒断流超时与恢复；EMOTIV 未启动 | `BrainLink-EditorSmoke.txt` / `.log`、`BrainLink-EditorBridge.log` |
| Windows x64 构建 | PASS；Build Finished, Result: Success | `BrainLink-Build.log` |
| Windows 成品移动 | PASS；半速实测 2.00 m/s，满速 4.00 m/s；平滑停止、信号差恢复、3 秒断流超时与恢复；EMOTIV 未启动 | `BrainLink-PlayerSmoke.txt`、`BrainLink-Player.log`、`BrainLink-PlayerBridge.log` |
| 原 MindWave 接入回归 | PASS；54 项协议／TCP／注册检查 | `MindWave-Regression.log` |
| MindWave 新组件入口 | PASS；原模拟器驱动真实主场景，半速／满速、实际位移、平滑停止，EMOTIV 未启动 | `MindWave-ComponentSmoke.txt` / `.log` |
| 原玩家与 EMOTIV 逻辑回归 | PASS；16,244 项检查，涵盖键盘、地形、边界及原控制逻辑 | `PlayerMovement-Regression.log` |
| PowerShell 工具 | PASS；准备、启动脚本语法解析无错误 | 准备脚本重复执行及 Python 测试结果 |

Editor 与 Windows 的完整模拟链路均使用 **仓库提供的真实 BrainLinkParser.pyd**，不是替代解析器。测试过程中没有安装虚拟串口驱动，没有购买或连接 BrainLink 设备。测试结束后已关闭模拟器和验证程序。

验证构建：`Builds/BrainLinkValidation/JurassicWorld.exe`。它使用临时验证场景并选中 BrainLink；生成场景在构建完成后已清理。源 `Assets/Scenes/Main.unity` 保持 MindWave Connector 选中、EMOTIV 与 BrainLink 关闭。

## SDK 依据

- [官方 Python 仓库](https://github.com/Macrotellect/BrainLinkParser-Python)：公开交付包含 README、Windows `.pyd`、macOS `.so` 与示意图片；没有 Python 解析实现源码或无设备模拟器。
- [固定版本](https://github.com/Macrotellect/BrainLinkParser-Python/commit/066af71079bc9eb87a6b9dcd4517b8817715ef38)，`.pyd` SHA-256：`d9340d07031993cca7b68e6bd720cb6cc8ac4e795c26d93b18c7202a8d835319`。
- 已确认 Windows PE 目标为 AMD64，依赖 `python311.dll`。官方 `parse(bytes)` 接收合成汇总包后正确返回 attention 与 signal。
- [官方 Unity SDK](https://github.com/Macrotellect/BrainLinkProSDK_Unity) 明确仅支持 iOS / Android，并将 Windows Unity 开发指向 Python 仓库。

## 验证范围

这些结果证明：官方解析器、Python 到 Unity 的数据链路、组件选择、玩家运动及 Windows 打包可用。

尚未验证真实设备的蓝牙配对、出站 COM、固件兼容性、真实更新频率、佩戴信号质量、人体 attention 变化和使用体验。EMOTIV 的硬件连接及账号授权也不属于本次软件回归。

桌面自动化辅助工具初始化失败，因此没有声称完成人工式鼠标点击或截图检查；Inspector 的互斥与 Undo 通过原生序列化属性事件验证，Play 锁定通过真实 Inspector UI Toolkit 标题容器验证。

删除模块的约定为先移除 Main Camera 上的对应组件再删除目录。设备程序集只依赖共享 PlayerInput 契约，场景生成器按类型名发现可选组件；本次未复制完整工程再执行删除后的第二轮构建。
