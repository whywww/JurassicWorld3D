# 恐龙资产索引

所有物种统一位于 `Assets/Dinosaurs/<物种>/`。`Source/` 保存原始模型、贴图和厂商原包；`Prepared/` 保存转换后的资源、项目材质、派生动画、Prefab、头像和专用配置。原包内部目录与拼写保留，物种目录使用项目已有标识。

适配资源并非源资源的完整副本。可以直接使用的 FBX 和内嵌动画不重复复制；需要切换 Legacy、修正模型方向或转换格式时，才生成对应适配结果。运行时通过保存好的场景、Prefab 引用加载，编辑器通过完整 AssetDatabase 路径读取资源，无需恐龙 Resources 目录。

| 物种目录 | 中文名／状态 | Source 内容 | Prepared 内容／接入入口 |
|---|---|---|---|
| TRex | 霸王龙，启用 | Sketchfab FBX、贴图 | 项目材质、头像；Scene Builder |
| Velociraptor | 伶盗龙，启用 | `Sketchfab/Raptor` 为当前使用版本；`FerociousIndustries` 为保留的另一套原包 | 项目材质、头像；Scene Builder |
| Rajasaurus | 胜王龙，禁用 | Sketchfab FBX、贴图 | 项目材质；Scene Builder 保留禁用模板 |
| Argentinosaurus | 阿根廷龙，禁用 | Sketchfab FBX、贴图 | 项目材质；Scene Builder 保留禁用模板 |
| Pachycephalosaurus | 厚头龙，启用 | `FerociousIndustries` 完整原包 | Legacy Idle/Walk、红色 Prefab；Add or Update Red Pachycephalosaurus |
| Stegosaurus | 剑龙，禁用 | `FerociousIndustries` 完整原包 | Legacy Idle/Walk、绿色 Prefab；Add or Update Green Stegosaurus（手动执行会添加该物种） |
| Baurusuchus | 包鲁鳄，启用 | FBX、原始贴图 | Run、材质、Prefab、头像；Add or Update Baurusuchus |
| Sauropelta | 蜥结龙，启用 | `Models/Sauropelta.fbx`、`textures` | Walk、材质、Prefab；Add or Update Sauropelta |
| Protoceratops | 原角龙，启用 | 原始 GLB/glTF、贴图及来源信息 | 转换 FBX、WalkCycle、三种材质、Prefab、头像；Add or Update Protoceratops |
| Pteradactal | 翼龙，启用 | 原始 GLB，含骨架与内嵌贴图 | 转换 FBX、提取贴图、打包贴图、双面 Shader、Flight、材质、Prefab、LowFlightAirspace；Add or Update Flying Pteradactal |

## 来源与许可

来源链接、作者及现有许可记录见项目根目录 README 的恐龙资产表。TRex、当前伶盗龙和翼龙记录为 CC BY；Ferocious Industries 资源记录为 Standard Unity Asset Store EULA；包鲁鳄和蜥结龙记录为 Fab Standard License。胜王龙和阿根廷龙许可记录仍待补充，本次整理不推定其许可。保留原包中已有的署名和许可文件。

## 维护与重建

- 下载 ZIP 统一放 `SourceArchives/Dinosaurs/<物种>/`，位于 Assets 外，继续被 Git 忽略。当前本地保留包鲁鳄、蜥结龙、翼龙和原角龙 ZIP；克隆仓库后正常运行不需要这些 ZIP。
- 项目材质和头像生成到各物种的 Prepared。不要手动覆盖 Source 中的厂商模型或把转换结果写回 Source。
- 旧四种 FBX 可由 Scene Builder 读取 Source 并生成项目材质；无需为了目录一致创建新的动画副本或 Prefab。
- 翼龙先在项目根目录执行 `blender --background --python Tools/Pteradactal/convert_model.py`，再执行 Unity 的翼龙接入菜单。转换工具从 SourceArchives 读取 ZIP，将 GLB 写入 Source，其余转换结果写入 Prepared。
- 各物种接入菜单位于 `Jurassic World > Scene`。完整 Bake 会重建场景；目录整理和普通启动不需要 Bake。
- `Jurassic World > Assets > Reimport Dinosaur Models` 搜索整个恐龙目录。原有模型导入配置通过 .meta 保留；通用 Legacy 默认导入规则只适用于指定来源的新导入文件，不覆盖厂商原包。
- 历史报告和检查点中的旧路径保留为当时记录，不作为当前生成路径。

## 验证

Unity 批处理入口：`JurassicWorld.Editor.DinosaurAssetOrganizationAcceptance.VerifyAndBuild`。检查所有物种模型路径、现有适配 Prefab、Main 中模型/动画/材质/头像引用，然后构建开发版 Windows Player，供现有翼龙运行冒烟测试使用；不会 Bake 或保存 Main。

重复生成检查入口：`JurassicWorld.Editor.DinosaurAssetOrganizationAcceptance.VerifyGenerators`。六种适配生成器各执行两次，检查文件集合不变，随后恢复原始 Prepared 文件内容，再校验场景引用。
