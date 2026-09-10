# 3D 侏罗纪世界 MVP

Unity 版本：`2022.3.62f3c1`。场景包含霸王龙、胜王龙、伶盗龙、阿根廷龙和翼龙：四种地面恐龙在草地中活动，翼龙在空中巡游，所有物种都支持视野内召唤和独立信息卡。

## 启动

1. 使用 Unity Hub 或 `C:\Program Files\Unity 2022.3.62f3c1\Editor\Unity.exe` 打开本目录。
2. 等待 Package Manager 完成 glTFast 依赖恢复和脚本编译。
3. 打开 `Assets/Scenes/Main.unity`，点击 Play。

首次恢复依赖需要能够访问 GitHub。若 Package Manager 失败，请确认 `Packages/manifest.json` 中的 glTFast `v6.4.0` 地址可访问后执行 Resolve。

## 操作

- `W/S` 或上/下方向键：按当前行进方向前进/后退，不改变视角。
- `A/D` 或左/右方向键：持续向左/右转向并前进，相机平滑跟随。
- 按住鼠标右键并移动鼠标：缓慢转动视角。
- `Space`：召唤当前画面内的恐龙。

恐龙会在静止与随机巡游之间切换。召唤后，视野中的恐龙会转向相机并沿固定路线穿过观察者，到达相机后方后恢复自由活动。信息卡会在召唤对象进入画面中央时淡入，离开主要视野后隐藏。

## 验收清单

- Console 没有 C# 编译错误或模型加载错误。
- 五种恐龙材质、蒙皮正常；地面恐龙有循环动作，翼龙持续播放飞行动画。
- 相机移动、转向跟随和右键视角控制平滑，且不会越过草地边界。
- 空格键只触发一次召唤；当前视野中的恐龙穿过视角后自动恢复随机活动。
- 信息卡只在召唤后且霸王龙位于画面中央、距离合适时出现。

可在菜单执行 `Jurassic World > Validate MVP`，或用批处理方法 `JurassicWorld.Editor.JurassicProjectSetup.ValidateProject` 检查模型、动画和场景。

## 当前边界

- 仅保证 Unity Editor 演示，不生成独立程序。
- 使用标准单相机和旧版 Input API；未接入 BCI、裸眼 3D 厂商 SDK、NavMesh 或多恐龙选择。
- GLB 或 FBX 加载失败会在 Console 和屏幕顶部明确报错，不使用占位模型冒充成功结果。

## 资产署名

`Rampaging T-Rex.glb` 来自 Sketchfab 的 [Animated Tyrannosaurus Rex Dinosaur Running Loop](https://sketchfab.com/3d-models/animated-tyrannosaurus-rex-dinosaur-running-loop-38007d947ae74dea83988cb0b08ee053)，按原页面标注的 CC BY 许可使用。发布或展示时请保留作者署名及原始资源链接。

新增模型来自用户提供的 `animated-rajasaurus-free.zip`、`pbr-velociraptor-animated.zip`、`animated-flying-pteradactal-dinosaur-loop.zip` 和 `animated-argentinosaurus-free.zip`。发布前请按各下载页许可补全作者署名和原始链接。
