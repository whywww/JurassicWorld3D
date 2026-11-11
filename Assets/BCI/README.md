# 脑机接口模块

主场景通过 Main Camera 上的组件互斥选择设备。停止 Play 后勾选 EMOTIV、MindWave 或 BrainLink；全部关闭时仅使用键盘。WASD / 方向键优先于脑控。设备连接和测试操作见各设备 README。

| 模块 | 使用说明 | 项目代码与配置 | SDK / 外部依赖 |
|---|---|---|---|
| Emotiv EPOC X | [Emotiv](Emotiv/README.md) | `Emotiv/Runtime`、`Editor`、`Settings`、`Tests/Editor` | [官方 Unity 插件](../Plugins/Emotiv-Unity-Plugin/README.md)，运行时需要本机 Launcher / Cortex Service |
| MindWave Mobile 2 | [MindWave](MindWave/README.md) | `MindWave/Runtime`、`Editor`、`Resources`、`Simulator`、`Tests` | 官方 ThinkGear Connector；SDK ZIP 由提取脚本读取，不导入 Unity |
| BrainLink | [BrainLink](BrainLink/README.md) | `BrainLink/Runtime`、`Editor`、`Python`、`Tests` | Python 准备脚本按固定版本及 SHA-256 下载官方解析器 |

## 目录约定

- `Runtime/`：设备协议、连接、输入映射及配置类型；`Editor/`：Inspector 和编辑器工具。
- `Settings/`：场景显式引用的共享配置资产。只有确实依赖 Resources 加载的资源才放在 `Resources/`；MindWave 的 Resources JSON 保留用于旧版 API 兼容。
- `Tests/Editor/` 与 `Tests/Runtime/`：按执行环境组织验收和烟测；Python、PowerShell 测试可直接位于 `Tests/`。
- `Simulator/`、`Python/`：按实际需要存放模拟器或外部桥接工具，不为每种设备创建空目录。
- README 记录安装与使用，Validation 记录有证据的验证结果。设备目录不复制官方 SDK，不存放生成的 Python 环境或构建产物。

## 共用接口与第三方依赖

[Assets/PlayerInput](../PlayerInput/) 保留设备无关的输入注册、互斥选择和共用 Inspector 逻辑；玩家移动控制器通过此接口读取输入。设备模块不互相依赖。

Emotiv 官方插件保留在 `Assets/Plugins/Emotiv-Unity-Plugin/`，维持现有编译顺序、DLL 导入设置和厂商目录结构。来源、许可证及项目补丁见该目录的 LICENSE 和 LOCAL_CHANGES.md。Emotiv 项目代码仍使用现有预定义程序集；此次目录整理不增加 asmdef，也不改变类型名称或命名空间。

MindWave 的 `Windows-Developer-Tools-3.2.zip` 保留在项目根目录，提取结果位于系统临时目录；BrainLink 的 SDK 和 Python 环境由脚本生成在忽略的 `Builds/BrainLinkTools/` 下。它们属于外部工具依赖，不应作为 Unity 原生插件导入。

Emotiv 凭据继续从根目录 `emotiv.local.json` 读取，该文件由 Git 忽略；提交的示例为 `emotiv.example.json`。凭据不进入 Assets。

移动 Unity 文件时必须同时保留原 `.meta` 文件和 GUID。设备详细文档中的菜单与 Batch 方法是验证入口；仅移动目录不会改变这些入口。项目场景集成与玩家移动回归测试可能引用玩家类型，不应因此让运行模块依赖场景代码。
