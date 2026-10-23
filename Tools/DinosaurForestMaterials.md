# 恐龙皮肤与森林融合

`Assets/Editor/DinosaurForestMaterials.cs` 集中定义 10 个物种的皮肤色调、光滑度和法线强度。场景构建和各物种独立的资源准备流程都会应用同一配置；不添加运行时组件。

- 身体使用原有贴图，按物种分别调整。眼睛、角膜、牙齿、爪子及其他独立部位的材质保持原样。
- 肿头龙和剑龙的身体材质复制到各自 `Prepared/ForestMaterials`，不修改供应商原始材质。
- 原角龙、蜥结龙的高光 RGB 转为较低的皮肤反射率，保留原光滑度 alpha。生成图为线性数据，重复生成不会累积压暗。
- 翼龙保留双面着色器，新增法线强度参数；身体投影和接收阴影在编辑态设置。
- 禁用物种保持禁用；其已有 Prepared 材质也会校准，重新构建时继续使用同一配置。

## 更新与检查

保存场景并退出 Play 后，执行菜单 `Jurassic World/Scene/Blend Dinosaur Skins with Forest`。该操作更新 Prepared 资源与 Main 场景，输出 `Logs/acceptance/dinosaur-skins/report.txt` 和截图。

批处理入口（需要图形设备，不使用 `-nographics`；入口完成后自行退出）：

```powershell
& 'C:/Program Files/Unity 2022.3.62f3c1/Editor/Unity.exe' -batchmode -projectPath E:/Unity/JurassicWorld -executeMethod JurassicWorld.Editor.DinosaurForestMaterialAcceptance.RunBatch -logFile E:/Unity/JurassicWorld/Logs/dinosaur-forest-materials.log
```

检查包括三档画质、近远景与原森林位置截图、重复应用 Prepared 和场景配置、材质从默认值重建的一致性，以及眼睛等独立材质、模型变换、行为和光照保持不变。截图使用临时模型副本和固定姿态，副本不会保存到场景中。原有低画质关闭阴影的策略保持不变。

第一次执行的 `before` 图来自执行前的材质；后续执行的 `before` 图对应再次执行前的状态。需要与版本库原始材质对比时，可临时将原 `.mat` 内容按其原 GUID 命名，放入 `Assets/DinosaurSkinReviewBaseline`；批处理结束会删除此临时目录。最终截图保留在 Logs 中，不纳入版本库。
