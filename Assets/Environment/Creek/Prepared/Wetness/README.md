# 雨后溪岸湿润材质 — 阶段四

状态：已实施，等待用户手动 Play 查收。阶段五尚未开始。

## 改动

- 56 个 Creek Mossy Rock 渲染器使用独立湿岩材质，复用原法线及已正确打包的平滑度贴图。
- 18 个 Creek Fern 渲染器使用独立双面镂空材质，保留原透明度、法线与纹理，仅轻微增加湿润高光。
- 湿润遮罩根据已有 Flow Point 生成，离溪流超过约 5 米为零；高度、噪声与苔藓颜色进一步减弱湿岩效果。
- 岩石非金属，湿处略暗且较平滑，苔藓仍粗糙；枯木和其他共享材质保持原样。
- 原 JungleCreekBank 层在当前地形的权重全为零，因此本阶段补上局部渐变湿泥权重。核心区域最多约 60%，其余原涂层按比例保留；没有整片覆盖森林地面。
- 湿泥使用独立地形层及较暗、低饱和的纹理副本，alpha 保存局部平滑度变化。
- 两处 Custom Reflection Probe 使用编辑态生成的 128 面分辨率 HDR cubemap，不在运行时刷新。
- 反射烘焙排除水面、雾、远景和恐龙；沿用现有 Crest 水面，不新增水面或平面反射系统。

## 查收

打开 Assets/Scenes/Main.unity，直接 Play，无需 Bake。先用中档，再用 F10 比较高档：

1. 沿东侧小溪往北走，观察石头是否有克制的湿润高光，而非统一塑料反光。
2. 靠近、转身和侧看石头，检查法线纹理与苔藓细节是否保留，暗面是否过黑。
3. 看溪岸泥土向草地的过渡，是否局部、平顺，有无过宽的沙地感或明显重复。
4. 侧看蕨叶，检查透明边缘、背面与高光是否正常，是否闪烁或过亮。
5. 比较枯木、泥土、苔藓、水面的粗糙度差异，确认现有水面表现正常。

## 配置与回退

WetSurfaces.asset 保存岩石、蕨叶湿润强度及泥土平滑度；修改后使用增量应用菜单：
Jurassic World > Environment > Creek > Apply Stage 4 - Wet Surfaces。

此操作只更新溪岸材料、局部地表权重和本阶段反射，不重建场景。
场景中的 Stage 4 - Creek Wet Surfaces 节点保存原材质、原地形层引用和局部涂层备份路径，无运行时更新逻辑。

精确撤回本阶段可使用：
Jurassic World > Environment > Creek > Restore Stage 4 Original Surfaces。
该菜单恢复原渲染器材质、原溪岸层及遮罩范围内的原地表权重，移除本阶段反射，并关闭后续自动应用。
必须保留 Reports/Creek/StageFour/OriginalControls-*.bin；若后续修改了溪岸涂层，撤回会还原这一区域的绘制，请先备份。

Reports/Creek/StageFour 同时保存本阶段开始前的完整主场景与地形资产。
直接覆盖检查点会丢弃其后的相应人工改动，因此优先使用局部恢复菜单。

## 检查结果

Unity 编译、着色器、保存后引用与低/中/高档短暂 Play 渲染通过。
地形高度、树实例、草地细节与导航指纹保持一致；实际记录的湿润遮罩外涂层变化为零。
原岩石/蕨叶材质、原 JungleCreekBank、Crest 水面均未改写；Play 退出后相关源文件不漂移。
截图在 Reports/Creek/StageFour，日志在 Logs/creek-stage4.log 和 Logs/creek-stage4-smoke.log。
以上不替代用户手动视觉查收，不含固定路线性能采样或演示机测试。

## 下一阶段

阶段五声音素材下载链接见 Tools/AfterRainStageFiveDownloads.md，放入 Assets/Environment/Audio/Source/AfterRain。
等待用户完成本阶段查收并明确同意，且素材准备完成后再继续。
