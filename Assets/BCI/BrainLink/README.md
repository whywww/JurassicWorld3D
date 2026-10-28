# BrainLink 使用说明

已验证模拟专注度数据经官方 SDK 解析后，可控制本项目中的玩家前进和停止。尚未进行实体设备测试，蓝牙连接和实际佩戴控制效果仍需验证。

## 1. 运行方式与数据流程

运行时需要同时启动以下两个程序：

1. Python 数据程序：读取并解析 BrainLink 数据，将 attention（专注度）发送给游戏。
2. Unity 游戏：接收 attention，并将其转换为玩家的前进速度。

无设备测试时，Python 程序生成模拟数据。接入实体设备后，改用串口读取模式；Unity 继续使用同一个 BrainLink 组件。

模拟测试的数据流程如下：

```text
指定测试值，例如 attention = 70
    ↓
Python 按设备数据包格式编码
    ↓
官方 BrainLink SDK 解析数据包
    ↓
SDK 解析出 attention = 70
    ↓
Python 将解析结果发送给 Unity
    ↓
玩家开始前进
```

该流程用于验证官方 SDK 的数据解析、Python 到 Unity 的传输，以及玩家移动控制。

测试中的 attention 由程序设定，不是根据人体脑电计算得出的数值。上述结果可用于确认软件链路是否正常，实际佩戴后的控制稳定性仍需通过实体设备测试评估。

## 2. BrainLink Pro 可解析的数据

本项目使用的官方 Python SDK 为 BrainLink Pro 提供以下数据接口：

| 数据 | SDK 字段 | 说明 |
|---|---|---|
| 专注度 | `attention` | 当前用于控制玩家前进速度 |
| 佩戴信号质量 | `signal` | 用于判断传感器接触情况；本项目仅在值为 0 时允许脑控前进 |
| 放松度 | `meditation` | 设备输出的放松度指标 |
| 八个脑电频段功率 | `delta`、`theta`、`lowAlpha`、`highAlpha`、`lowBeta`、`highBeta`、`lowGamma`、`highGamma` | 各频段的功率数据 |
| 原始脑电采样 | `raw` | 通过独立回调输出，可用于记录或绘制原始脑电波形 |
| 三轴运动数据 | `x`、`y`、`z` | Python SDK 称为陀螺仪数据，通过独立回调输出 |
| 心搏间期相关数据 | `rr1`、`rr2`、`rr3` | 通过独立回调输出 |
| 扩展数据 | `ap`、`battery`、`version`、`gnaw`、`temperature`、`heart` | 分别对应喜好度、电量、版本、咬牙、温度和心率相关字段 |

字段名称以[官方 Python SDK](https://github.com/Macrotellect/BrainLinkParser-Python) 为准。喜好度、电量、心率和温度等含义可参考[厂商 Pro SDK 说明](https://github.com/Macrotellect/BrainLinkPro_Android/blob/master/doc_AndroidSDK_cn.md)。Python SDK 的字段说明未完整列出各字段的单位、数值范围和更新频率，接入时需进一步确认。

当前脚本仅向 Unity 转发 `attention` 和 `signal`，另附 `mode` 标记数据来自模拟器还是实体设备。`mode` 由本项目添加，不是设备输出的字段。

上述列表表示 SDK 提供的解析接口。实体 BrainLink Pro 是否持续输出全部字段，以及各字段的实际单位和更新频率，仍需连接设备验证。

## 3. 无设备测试步骤

### 第一步：准备 Python 环境

首次使用、迁移到其他电脑或删除 `Builds/BrainLinkTools` 后，需执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Assets\BCI\BrainLink\Python\Setup-BrainLink.ps1"
```

首次准备需要网络连接，且电脑上需已安装 Conda。脚本会创建本项目专用的 Python 3.11 环境，并准备串口读取工具和官方 SDK，无需修改系统默认 Python。

成功后会显示：

```text
BrainLink SDK / Python 3.11 x64 / pyserial ready
```

### 第二步：启动模拟数据

执行以下命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Assets\BCI\BrainLink\Python\Start-BrainLink.ps1" -Attention 70
```

运行期间需保持该 PowerShell 窗口打开。程序每秒生成并输出一次模拟 attention 和 signal，同时向已连接的游戏发送数据。

`-Attention 70` 将模拟专注度固定为 70。

### 第三步：在 Unity 中选择 BrainLink

1. 打开主场景 `Assets/Scenes/Main.unity`。
2. 在 Hierarchy 中选中 **Main Camera**。
3. 在 Inspector 中找到 **Brain Link Player Input**，勾选组件标题左边的启用框。
4. **Emotiv Player Input** 和 **Mind Wave Player Input** 会自动取消勾选，确保仅启用 BrainLink。
5. BrainLink 参数保持默认：Host 是 `127.0.0.1`，Port 是 `13856`，Attention Threshold 是 `40`，Sample Timeout 是 `3`。
6. 进入 Play。验证脑控移动时不按 WASD 或方向键，键盘输入具有更高优先级。

项目保存的初始状态为启用 MindWave。启动 Python 不会改变 Unity 的设备选择，需要在 Inspector 中启用 BrainLink。

### 第四步：检查运行状态

游戏中的 BrainLink 状态面板应显示：

- `mode: simulate`：当前使用模拟数据。
- `attention: 70`：当前专注度。
- `signal: 0`：模拟的佩戴信号良好。
- `forward target: 0.50`：目标前进速度是满速的一半。

玩家应开始前进，速度稳定后约为 2 m/s。F9 可切换状态面板的显示。

### 第五步：调整测试数据

在运行模拟器的 PowerShell 窗口中输入以下命令，每条命令输入后按回车执行：

| 命令 | 预期结果 |
|---|---|
| `a 100` | 玩家加速到满速，当前主场景约 4 m/s |
| `a 70` | 玩家降到半速，约 2 m/s |
| `a 40` | 玩家减速停止 |
| `s 200` | 模拟佩戴信号差，玩家停止 |
| `s 0` | 模拟信号恢复；收到下一个有效数据后恢复前进 |
| `pause` | 模拟设备不再发数据，约 3 秒后开始减速停止 |
| `resume` | 恢复发数据 |
| `drop` | 模拟连接断开，游戏会自动尝试重连 |
| `quit` | 关闭模拟器 |

signal 表示佩戴信号质量。本项目仅在 `signal = 0` 时允许脑控前进，非零值会使目标前进速度归零。

也可关闭当前模拟器，使用以下命令启动自动循环测试：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Assets\BCI\BrainLink\Python\Start-BrainLink.ps1" -Auto
```

自动模式每隔约 4 秒切换测试条件，依次覆盖停止、半速、满速、信号差、数据中断和重连。可先使用固定值 70 确认连接正常，再运行自动测试。同一端口仅运行一个数据程序。

## 4. 实体设备接入步骤

Unity 继续使用 **Brain Link Player Input** 组件。Python 程序切换为串口模式，读取实体设备的数据。

### 第一步：完成 Windows 蓝牙配对

1. 开启 BrainLink Pro，并按设备说明完成佩戴。
2. 在 Windows 蓝牙设置里添加设备。按官方 SDK 的说明，选择带耳机图标的 BrainLink Pro。
3. 在蓝牙的更多设置 / COM 端口列表中，找到设备对应的 **出站端口（Outgoing）**，记录端口号，例如 `COM7`。
4. 关闭可能占用设备串口的其他软件，并停止模拟器，避免数据服务端口冲突。同一串口不能同时被两个程序占用。

COM7 为示例，需替换为当前电脑显示的实际出站 COM 端口。若未出现出站 COM，需先检查设备配对是否完成。

### 第二步：启动设备数据读取

以出站端口 COM7 为例，执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Assets\BCI\BrainLink\Python\Start-BrainLink.ps1" -Mode serial -SerialPort COM7
```

运行期间保持 PowerShell 窗口打开。连接成功后，应显示串口连接提示，随后持续输出解析得到的 attention、signal 和 `mode: serial`。

确认持续收到设备数据后再启动游戏。`Serial connected` 仅表示串口已打开，仍需检查是否产生有效的数据输出。

### 第三步：检查 Unity 中的设备数据

1. 停止 Unity Play 模式，在 Main Camera 上选择 **Brain Link Player Input**。
2. 进入 Play，检查状态面板是否显示 `mode: serial`。
3. 确认佩戴信号为 `signal: 0`。
4. 观察 attention 是否变化。默认 attention 超过 40 时开始前进，数值越高，目标速度越高。

实体设备的数据流程如下：

```text
BrainLink Pro → Windows 蓝牙串口 → Python + 官方 SDK → Unity → 玩家前进
```

该接入方式不依赖 ThinkGear Connector 或虚拟串口驱动。

若设备输出的 attention 长期低于 40，可在停止 Play 后调整 BrainLink 组件中的 Attention Threshold。调整前需先确认佩戴信号良好，且 attention 持续更新。

## 5. Windows 程序部署

独立运行时同样需要启动 Python 数据程序和游戏 exe。当前游戏不会自动启动 Python。

### 运行现有验证构建

现有验证构建位于：

```text
.\Builds\BrainLinkValidation\JurassicWorld.exe
```

该构建已启用 BrainLink。

1. 按前述命令启动 Python：无设备时使用模拟模式，连接实体设备时使用 `-Mode serial -SerialPort 实际COM号`。
2. 保持 PowerShell 窗口运行，双击上述 exe。
3. 通过状态面板确认当前模式为 `simulate` 还是 `serial`，以及 attention、signal 是否正常。

正常运行时直接打开 exe，不添加 `-brainlinkSmoke` 参数。该参数用于自动测试，测试结束后程序会自动退出。

### 重新构建游戏

1. 在 Main 场景中勾选 BrainLink，确认另外两个设备关闭。
2. 保存场景，使构建使用当前设备选择。
3. 在 Unity 的 Build Settings 中把 Main 设为启动场景（已勾选场景列表的第一项），平台选择 Windows x64，然后 Build。
4. 运行时仍然先开 Python，再开新生成的 exe。

部署时需复制完整的 Unity 构建目录，包括 exe、`_Data` 文件夹和其他运行文件。

### 部署到其他电脑

部署时保留原有目录结构，并复制以下内容：

- 完整的游戏构建文件夹。
- 项目下 `Assets/BCI/BrainLink/Python` 文件夹，里面有准备脚本、启动脚本和 Python 程序。

目标电脑需已安装 Conda。在目标位置运行 Setup-BrainLink.ps1 重新创建环境，不直接复制原电脑的 python-env 文件夹。脚本会根据自身位置，在对应项目目录的 `Builds/BrainLinkTools` 下准备环境和 SDK。

迁移后保留 `Assets/BCI/BrainLink/Python` 的相对目录结构，并在目标项目根目录执行本文命令。首次准备需要联网。

迁移后需重新完成蓝牙配对，并确认实际 COM 端口号。

## 6. 常见问题与排查顺序

| 现象 | 检查项 |
|---|---|
| 提示 `Run Setup-BrainLink.ps1 first` | 先运行一次准备环境的命令 |
| 准备时提示 `Conda not found` | 默认查找用户目录下的 `miniforge3/Scripts/conda.exe`；Conda 装在其他地方时，给准备命令追加 `-Conda "conda.exe 相对项目根目录的路径"` |
| 提示端口已被占用 | 检查是否存在其他模拟器或设备读取程序占用同一端口，关闭重复进程 |
| Python 有数字，游戏不动 | 是否已启用 BrainLink；Host / Port 是否保持 `127.0.0.1` / `13856`；signal 是否为 0；attention 是否超过阈值 |
| 接入实体设备后仍显示 `simulate` | 停止模拟器，使用带 `-Mode serial` 的命令启动设备读取程序 |
| 真实设备一直提示串口不可用 | COM 号是否正确、是否为出站端口、设备是否开机，以及其他软件是否占用了串口 |
| 串口连接成功，但没有 attention 输出 | 继续检查设备连接和输出；此时还不能判断是 Unity 的问题 |
| attention 有数值，但 signal 不是 0 | 先检查佩戴和传感器接触；当前规则会让玩家停止 |
| 想切换设备，但勾选框不能改 | 先停止 Play，再选择设备 |

排查顺序为：确认 PowerShell 持续输出数据，检查 Unity 是否接收数据，最后确认 signal 和 attention 是否满足前进条件。