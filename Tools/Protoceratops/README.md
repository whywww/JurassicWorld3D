# 原角龙接入

Main 场景包含一只约 2 米的 Protoceratops（原角龙）。使用压缩包原生 WalkCycle；原始关键帧区间为 0.5–2.5 秒，转换保留完整的 2 秒步行循环，不包含起始静止等待。原生步速由四只脚采样约为 0.281 米/秒，移动和动画同步。使用与最新蜥结龙相同的 `RoamingMode.ContinuousFree`：持续步行、行进中平滑转弯、提前绕障和目标接续；取消随机休息，只有无安全通路时冻结步态，通路恢复后继续前进。不启用脚部锁定或玩家互动。

## 资源和重建

- 原包归档：`SourceArchives/Dinosaurs/Protoceratops/Protoceratops.zip`；原始资源在 `Assets/Dinosaurs/Protoceratops/Source`，适配资源在同级 `Prepared`。
- 在项目根目录执行：`blender --background --python Tools/Protoceratops/convert_model.py`。
- Unity 菜单：`Jurassic World > Scene > Add or Update Protoceratops`。重复执行只更新该物种，不重建地图。完整 Bake 同样包含原角龙。
- 转换仅删除 Blender 的无材质辅助 Icosphere，保留眼球和角膜。原包眼部是独立静态网格，先将起始帧（第 12 帧）的头骨姿态逆变换到绑定空间，再给全部顶点赋予头骨的刚性权重，保证起始位置和原始 UV 不变。
- 使用 Built-in Standard 材质：身体用漫反射、法线、AO、独立镜面反射和粗糙度转换的 SpecularSmoothness；眼睛使用原始眼球贴图，角膜使用透明高光。损坏的 ORM 仅保存在 Source，不参与渲染。
- 来源：原包 `info.json` 记录作者 Kyan0s、模型 Protoceratops Andrewsi 及原始地址。原包未附许可文本，不推定许可。

## 行为和验证

直接复用 `DinosaurAgent.ContinuousRoam` 通用持续漫游实现，不添加原角龙专用转向或绕行分支。原有前视距离 3 米、最小转弯半径 2.5 米、绕障探测距离 3 米、转速变化上限 30 度/秒²均保留；保留导航身体扫掠、安全出生、同伴会车，以及对霸王龙 15 米避让、20 米解除的规则。物种枚举只在末尾追加，保留已有序列化值。

验收菜单：`Jurassic World > Acceptance > Verify Protoceratops Main Roaming`；批处理入口为 `JurassicWorld.Editor.ProtoceratopsAcceptance.Run`。直接读取已保存的 Main，不重新生成原角龙或地图。检查尺寸、导航、材质、2 秒原生动作、骨骼运动，以及至少 120 秒的持续移动、目标接续、行进转弯、循环、接地及安全阻挡；正常漫游不得进入 Idle 或原地转身。测试用固定种子只作用于 Play，不写回 Main。

日志：`Logs/protoceratops-acceptance.txt`；截图：`Logs/protoceratops-model.png`、`Logs/protoceratops-walk.png`、`Logs/protoceratops-curve.png`（行进转弯），受阻时另保存 `Logs/protoceratops-stop.png`；步态采样：`Logs/footprobe_原角龙_WalkCycle.csv`。

## 眼球专项验收

`Jurassic World > Acceptance > Verify Protoceratops Eyes` 只检查已保存的 Main；完整漫游验收会先自动执行该检查。检查身体、眼球、角膜三块蒙皮网格、原始眼部顶点数量、眼球贴图与透明角膜材质，以及 0、0.5、1、1.5、1.99 秒时眼部相对头骨的位置。`EnsureAssets` 在原始眼球或角膜缺失时直接报错，防止只生成身体的 Prefab。

眼球 CPU 蒙皮采样沿用已有缩放补偿方式，只对原角龙新增的两个眼部网格生效；不改变运行时变换或原有身体采样。专项日志为 `Logs/protoceratops-eyes-acceptance.txt`；左右及正面近景为 `Logs/protoceratops-eye-{left,front,right}-{时间}.png`。
