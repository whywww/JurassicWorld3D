# 开发辅助工具

这些工具需要手动执行，不参与游戏运行。以下命令在项目根目录执行。

## 资源分析

- python Tools/extract_anim_speed.py：估算霸王龙行走动画速度；可用 --fbx <文件> 指定输入。
- python Tools/tex_tone_probe.py：采样地面和草地纹理颜色（依赖 Pillow）；可用 --textures <目录> 指定输入。

默认资源路径相对脚本位置定位，与当前工作目录无关。

## 编译和回归检查

先运行 Tools/CompileCheck/run_runtime_check.ps1，再运行 run_editor_check.ps1。
run_crossing_regression.ps1 检查路线交叉逻辑，可用 -CorridorSource <文件> 指定源码。

三个脚本均支持 -UnityEditorPath <Editor目录或Unity.exe>，其次读取环境变量 UNITY_EDITOR_PATH。
未指定时，从 Unity 生成的 Assembly-CSharp.csproj 引用自动发现编辑器位置。
使用 ProjectSettings/ProjectVersion.txt 指定的 Unity 版本；移动项目或编辑器后重新生成 IDE 项目文件。
无法定位编辑器或编译工具时，脚本会报错并提示如何配置。

针对个人缓存截图的 skyline 和 seam 分析脚本已删除。
