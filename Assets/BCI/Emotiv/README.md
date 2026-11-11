# Emotiv Headset：配置与连接

本项目采用官方 [unity-plugin 的 Option 1：Cortex Service](https://github.com/Emotiv/unity-plugin#option-1-emotiv-cortex-service-desktop-only)，通过 `wss://localhost:6868` 连接本机服务。当前接入组件支持 Windows Editor / Windows Player；**不要定义 `USE_EMBEDDED_LIB`**，无需 Embedded Cortex Library 或 UniWebView。插件及桌面依赖已包含在项目中。

## 1. 在 Launcher 中连接耳机

安装并启动 [EMOTIV Launcher](https://www.emotiv.com/products/emotiv-launcher)，登录自己的 EmotivID。按耳机型号完成无线适配器或蓝牙连接，确认 Launcher 中设备在线。佩戴耳机，并在设备质量界面检查电极接触质量；调整传感器，确认信号适合训练和检测。运行 Unity 时保持 Launcher 和 Cortex Service 运行，并使用训练 profile 所属的同一个账号。

## 2. 准备 Cortex 应用凭据及授权

通过 [EMOTIV Developer](https://www.emotiv.com/pages/developer) 注册 Cortex 应用；在账号的 Cortex 应用管理页面获取 **Client ID** 和 **Client Secret**。这两个字段属于开发者应用，不能用 EmotivID 的登录账号和密码替代。详细流程见 [Cortex Getting Started](https://emotiv.gitbook.io/cortex-api)。

首次启动本项目的脑控连接时，插件会请求应用访问权限；在 Launcher 中接受对应应用的访问请求，随后插件自动执行授权。账号或应用权限、许可不足时，以 Launcher 提示和 Console 中的 Cortex 错误为准。参见 [官方 API 流程](https://emotiv.gitbook.io/cortex-api/overview-of-api-flow)。

## 3. 准备 Mental Command profile

连接耳机本身不会产生可用的方向指令。打开 **EmotivBCI**，在同一账号下选择或创建训练 profile，先训练 **neutral**，再训练 **push、pull、left、right**；确认四个 action 已训练并激活，保存 profile。在 EmotivBCI 中先测试动作能被识别并保存 profile，然后断开 EmotivBCI 的设备连接、释放 profile；测试时可完全退出 EmotivBCI，再运行 Unity。仅退出训练模式或切换窗口可能仍占用 profile。保持 EMOTIV Launcher / Cortex Service 运行。本项目只加载 profile 和接收数据，不提供训练界面。

记录 profile 的准确名称（包括大小写）。`Guest001` 只是项目默认名称，**不表示项目附带已经训练好的 profile**。拼错名称时，插件可能创建新的空 profile，导致只有 neutral 或没有有效方向。训练方法见 [训练第一个 command](https://emotiv.gitbook.io/emotivbci/mental-commands/training-your-first-command) 和 [训练 profiles](https://emotiv.gitbook.io/emotivbci/training-profiles/your-training-profiles)。

## 4. 本地 JSON 配置

Emotiv 连接信息从项目根目录的 `emotiv.local.json` 读取；控制参数保存在项目配置资源中。首次配置时，将 `emotiv.example.json` 复制为 `emotiv.local.json`，按下面字段填写；已有本地配置时直接编辑即可。

| JSON 字段 | 用途／示例默认值 |
|---|---|
| `clientId` | Cortex 应用的 Client ID |
| `clientSecret` | 同一应用的 Client Secret |
| `appName` | 应用名称；`JurassicWorld` |
| `profileName` | 已训练 profile 的准确名称；示例为 `Guest001` |

`emotiv.local.json` 已加入 `.gitignore`。修改 JSON 后重新进入 Play，或重启 Windows Player，即可加载最新配置。文件缺失、JSON 无效或必填字符串为空时，脑控不连接，WASD 仍可用。

停止 Play 后，在 Main Camera 上勾选 **Emotiv Player Input**，**Mind Wave Player Input** 和 **Brain Link Player Input** 会自动取消勾选。主场景保存的选择为 BrainLink，使用 Emotiv 前需主动切换。三个组件全部取消勾选时仅使用键盘。

## Inspector 控制配置（随项目共享）

1. 打开 Main 场景，选中玩家相机，找到 **Emotiv Player Input**，勾选启用。
2. 停止 Play 后调整以下参数：

| 参数 | 使用说明 |
|---|---|
| Push / Pull / Left / Right | 选择各指令对应的动作，也可设为停止 |
| 移动速度 | 默认 4 米/秒 |
| 转向速度 | 默认 60 度/秒 |
| 接受阈值（Power） | 默认 0.3；越低越容易接受指令 |
| 指令超时 | 默认 1 秒；超过此时间未收到指令，视为无有效指令 |
| 无有效指令时前进比例 | 默认 0.5，即半速前进；设为 0 则停止 |

Power 达到接受阈值后执行动作，数值越大，移动和转向越快。

脑控连接正常但没有有效指令时，默认半速前进；将“无有效指令时前进比例”设为 0 可关闭。指令设为“停止”时会停止，不会自动前进。连接中断或组件禁用后，脑控停止移动。

按住 WASD 或方向键时，**键盘优先**；松开后恢复脑控。按住鼠标右键可调整视角。

每个指令可单独调整敏感度：不勾选 **使用项目值** 时沿用 Emotiv profile 设置；勾选后填写 1–10，数值越大越容易触发。请先在 Emotiv 中训练并激活对应指令。

调整后点击 **保存控制配置**，再进入 Play 测试。Play 期间无法修改参数，需先停止。

参数随项目共享，构建程序也会使用相同设置；敏感度调整不会保存到 Emotiv profile。其他电脑仍需单独准备连接凭据和训练 profile。

找不到配置时，点击 **关联项目默认控制配置**。

## 5. 启动并确认连接

保持耳机在线及 Launcher 登录，点击 **Play**，在 Launcher 接受首次访问请求，再查看 Unity Console 中的 `[EMOTIV]` 日志：

1. `Connecting to wss://localhost:6868`：开始连接；`Cortex state` 显示当前连接／授权阶段。
2. `Authorized; searching for headset / Virtual Brainwear`：授权完成；无设备时每 2 秒继续查询。
3. `Headset found` → `Session created; loading profile`：找到设备并建立 session。
4. `Profile loaded`：加载了指定 profile，开始订阅 `com/sys/dev`。
5. `Mental Command stream subscribed; control ready`：收到 `com` 订阅成功通知，可开始测试方向控制。

项目默认选择搜索结果中的第一个设备；测试时只保留目标耳机或目标 Virtual Brainwear。独立程序使用构建中保存的同一配置，并在运行前登录 Launcher；脑控参数目前通过 Editor 配置，没有独立程序内的配置界面。

## Virtual Brainwear 虚拟控制测试

1. 在 Launcher 中创建 simulated device，保持设备 powered on / connected，打开模拟数据控制面板。
2. 在 EmotivBCI 中创建例如 `SimGuest` 的 profile，不需要完成训练。参见 [官方 BCI 说明](https://emotiv.gitbook.io/cortex-api/bci)。
3. 断开 EmotivBCI 的设备连接并完全退出 EmotivBCI，保留 Launcher。检查其他 Cortex 示例、BCI 工具或独立游戏实例也没有占用该设备的 profile。
4. 在 `emotiv.local.json` 中设置 `"profileName": "SimGuest"`，确认对应 Cortex 凭据已填写，再重新进入 Play。
5. 等待 `Profile loaded: SimGuest` 及 `Mental Command stream subscribed; control ready`。在 Launcher 的模拟控制中选择 push/pull/left/right，将 power 设置为高于 Threshold，并让模拟器持续输出；返回 Unity Game 窗口，不按任何方向键，观察相机前进。
6. 用 `com data: push, power: ...` 日志确认收到的实际指令。改为 neutral 或 power 低于 0.3 后应平滑切换为正常速度的 50% 自动前进；同方向按键优先，不会与脑控叠加加速。

## 常见问题

| 现象 | 检查步骤 |
|---|---|
| 配置缺失提示 | 在非 Play 状态填写四个文本字段；重新进入 Play |
| 一直 Connecting / Service_connecting | 确认 Launcher 已启动、Cortex Service 正常、本机端口 6868 可用；检查插件的连接／TLS 错误；参考 [Cortex 连接说明](https://emotiv.gitbook.io/cortex-api/connecting-to-the-cortex-api) |
| Login_notYet / Authorize_failed | 登录 Launcher，确认应用凭据成对正确，接受访问请求，查看 Cortex 的许可或权限错误 |
| Authorized 后没有 Headset found | 确认设备在线及适配器／蓝牙连接；先在 Launcher 确认设备，再重新 Play |
| profile is loaded by other apps | 保存训练，断开或完全退出 EmotivBCI 及其他占用应用；保留 Launcher，然后重新 Play |
| 加载 profile 失败／只有 neutral | 核对账号和准确 profile 名称、设备兼容性、四个 action 是否已训练并激活；不要误用自动创建的空 profile |
| 持续收到 push 但未显示 control ready | 更新到修复后的 EmotivPlayerInput，停止并重新 Play；使用缓冲模式无关的 Cortex 订阅回执检测就绪 |
| 有 command 但不移动 | 查看插件 `com data` 日志中的 action 和 power；确认 power 达到 Threshold、没有按方向键、组件启用且引用正确 |
| 断连后没有恢复 | 输入会自动清除；确认耳机和 Launcher 恢复在线，必要时停止 Play 后重新进入，重新完成授权／session 流程 |

开发验证入口：**Jurassic World > Tests > Emotiv Input Acceptance**，检查四方向映射、阈值、速度缩放、键盘优先、超时和场景组件引用。另可运行 **Jurassic World > Tests > Emotiv Control Play Isolation**，自动检查预先保存的参数在普通/快速 Play 中生效，以及 Play 期间 Inspector 只读、配置文件不变。**Jurassic World > Tests > Emotiv Sensitivity Acceptance** 检查动作顺序、敏感度继承/覆盖、session 请求、重连和错误处理，不需要真实设备。该检查不代替真实耳机验证；完整验收需填写凭据后，用真实 Headset 或 Virtual Brainwear 走完上述连接日志并实际测试操作。

## 模块结构与依赖

本目录的 Runtime、Editor、Settings 和 Tests/Editor 分别保存项目运行代码、Inspector、共享配置资产和验收测试。共享配置位于 `Assets/BCI/Emotiv/Settings/EmotivControlSettings.asset`，通过场景引用使用。

官方 SDK 位于 [Assets/Plugins/Emotiv-Unity-Plugin](../../Plugins/Emotiv-Unity-Plugin/README.md)，保留其目录结构、编译方式和许可证。共用输入接口位于 `Assets/PlayerInput/`。项目凭据文件与示例继续保留在项目根目录。完整约定见 [脑机接口模块索引](../README.md)。
