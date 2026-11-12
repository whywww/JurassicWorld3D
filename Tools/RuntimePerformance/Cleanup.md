# 性能版本保留与 legacy 清理（2026-11-12）

范围：仅本轮性能相关旧路径、测试与专用资源。保留当前三档渲染/碰撞查询优化、F10 画质切换、已烘焙溪流与软植被清单、当前采样/诊断/报告工具。未清理其他玩法、脑控或尺寸相关测试。

## 已移除

- Baseline / Contact / Reflection / Water / Batching / Materials 分阶段开关、旧低画质阶段菜单，以及阶段专用的原材质/中间批处理着色器路径。
- 临时关闭平面反射的 Crest 材质副本；最终三档始终关闭平面反射及 Crest 模拟，并使用现有静态溪流。退出时仍恢复保存的组件状态。
- `AllTierPerformanceAcceptance`、`RuntimePerformanceAcceptance`、`AllTierPlaySmoke`、`LowQualityTerrainProbe` 及对应 meta；所有剩余源代码入口已解除引用。
- 三个旧 Batch 阶段着色器、DepthCapture 测试着色器及 meta；删除前检查均没有场景、预制体、材质或资源的序列化引用，生产 Cheap 着色器没有依赖这些文件。
- 两个 Python 测试模块、旧 Editor 60 FPS 比较脚本、ContactMathCheck / Check-ContactMath 测试工具。
- 固定且不能保证恐龙入镜的旧 `dinosaur` 视角；保留 `dinosaur-close`。

## 保留与验证

- 原优化包 `Builds/AllTierOptimized`、优化前包 `Builds/AllTierBaseline` 和正式测量包 `Builds/AllTierMeasured` 保持原样。原优化包 exe / 游戏程序集 SHA-256 与交付记录一致。
- 清理后 Release 程序：`Builds/AllTierClean/JurassicWorld.exe`，构建退出 0。三档默认仍为原生 1920×1080，当前阴影、LOD、接触弯折和软植被预算均保留。
- Runtime / Editor 独立编译均 CSC EXIT 0。修正独立编译工具读取未刷新 csproj 时仍包含已删除源文件的问题。实际 Unity 导入与 Release 构建同时通过。
- 三档分别对森林、溪流、活动恐龙近景和漫游完成 2 秒预热 / 3 秒采样短时检查，均有完成标记和有效可见帧；未用短测重新声明正式 FPS 提升。
- 三档均保持 6 只恐龙、775 个硬树阻挡；1757 株安全软植被中隐藏数分别为 878 / 527 / 175。五个原有尺寸修改文件的 SHA-256 未改变。
- 核对溪流截图，水面、叶片轮廓、原生 UI 及森林显示正常。正式前后数据仍以 `AllTierComparison.md` 为准。
- 详细校验值：`Logs/RuntimePerformance/performance-cleanup-verification.json`；短测目录 `Logs/RuntimePerformance/cleanup-{Low,Medium,High}`。

## 缓存例外

自动审批拦截了旧 `Tools/RuntimePerformance/__pycache__/compare_results.cpython-312.pyc` 文件的删除，仅返回 `blocked by policy`，未提供更具体原因。该缓存保留；对应源代码、测试与运行入口已删除。当前报告生成器的缓存也保留。

历史验收报告记录清理前完成的检查；其中旧测试命令已退役。现有尺寸修改、原始素材、场景及核心玩法数据未覆盖。
