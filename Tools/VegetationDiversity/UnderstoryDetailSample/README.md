# 中心林下细节样板

Unity 菜单：`Jurassic World > Environment > Understory Detail Sample`。

- **Build and Verify**：首次选址并构建；以后重放已冻结记录，不重新选择区域。
- **Rebuild**：按相同GUID、种子及绝对变换恢复，并重新检查当前净空。
- **Undo**：只移除 `Understory Detail Sample`，关闭配置，不恢复或改动旧树木。

配置：`Assets/Editor/UnderstoryDetailSample.json`。种子733101；样板窗口选择结果在 `WindowCandidates.csv`，新增对象逐项记录在 `Entries.csv`。`Layout.md` 记录实际数量、可布置干地、辅助包围盒覆盖和导航复核。`before/after-0..3.png` 分别为入口、近地、侧面、俯视的固定机位对照。编辑态截图不作为Crest水面绘制或动态性能证明。

布局复用中心补空冻结保护网格，额外执行实际水体/地面高度检查，不扩大岸边禁种带。腐木和苔石作为独立硬道具进入既有实体障碍及导航；木质灌木保留硬木，软叶接入原有枝叶交互。落叶和苔藓使用已有透明裁剪材质，分别合并成独立贴地网格，既不覆盖大块地形，也不修改共享材质、地形纹理或共享植物资源。

本轮新增植物独立设置LOD剔除，屏幕相对高度阈值0.02；关闭其小型叶片投影，保留现有树冠阴影及接收阴影。LOD仅影响显示，硬木和道具障碍不会随显示剔除。未缩小原有枝叶交互系统的可达范围。`AssetAudit.csv` 报告实际植物高度与材质；硬道具即使没有Physics Collider，也通过显式实体包围盒参与现有恐龙碰撞与导航。

实施前副本在 `Checkpoint/`；不要用旧Main直接覆盖其他任务后续修改。日常恢复使用独立撤销入口。完整Bake在原植被生成后接入本样板记录，记录已不安全时停止；完整Bake与单独记录重放的验收结果须分别记录。

## 后续资源候选（本轮未下载或导入）

[Low poly vegetation for games — denoises](https://sketchfab.com/3d-models/low-poly-vegetation-for-games-cdac82eb61ab4d83bf162b4c44b44d6d)：页面检索信息列出13种植物、整包约9千三角面、CC Attribution。候选用途为现有蕨类之外的叶形变化。下载前人工复核当前授权及下载状态，保留作者、页面和授权原文；导入后再检查具体物种、单位、材质、贴图、透明过绘、LOD和当前雨林风格。未通过这些检查前不纳入生成器。

[Cut branches and leaves heap — matousekfoto](https://sketchfab.com/3d-models/cut-branches-and-leaves-heap-31a023461b2f4b1fb912b25e86acb586)：页面列出约18.6万三角面、4张8K贴图、CC Attribution。只作细节参考，不直接用于本样板批量散布。

本轮先验收现有资源组合，再决定是否手动下载第一项做独立小范围比较；不自动购买资产。
