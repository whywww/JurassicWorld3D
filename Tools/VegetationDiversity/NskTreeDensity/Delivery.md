# NSK tree01–04 密度调整

主场景采用参考点 (14.8, -4.4) 的树根密度：半径5米3棵，半径10米10棵。保护参考点半径10米内所有树，以及原本在两个尺度均未超标的树。树冠允许自然交叠。

全场 NSK 树由2212棵减至799棵，本轮撤下1413棵（场景对象288棵、Terrain实例1125棵）。过密示例 (53.5, -21.5) 的5米邻域由5棵减至2棵，10米邻域由12棵减至6棵。

仅撤下选中的 NSK 树；保留树不移动、不缩放。场景对象停用并保留原有材质与组件；Terrain实例记录位置、原型索引、顺序索引、尺寸、朝向、颜色和光照颜色。此前的局部撤下记录独立保留。

Unity 菜单：`Jurassic World > Environment > NSK Tree Density`。

- `Apply and Verify`：应用规则，检查重复执行、从JSON恢复后撤销、重新应用，以及植被重建后处理入口。
- `Undo This Thinning`：恢复本轮撤下的树，停用本轮规则。恢复前需保留原场景对象和Terrain原型；若之后手动删除了原对象，恢复会报错。

规则接在统一植被准备流程和独立东南补植流程后，目标密度固定记录在 `Assets/Editor/NskTreeDensity.json`，后续重建不重新估算参考密度。障碍分层、导航和枝叶交互数据随场景更新。

保护约束优先于密度上限；17个采样窗口仍略超目标，列在 [ResidualWindows.csv](ResidualWindows.csv)。其余数量见 [Summary.md](Summary.md)，逐树记录见 [Trees.csv](Trees.csv)，检查结果见 [Validation.md](Validation.md)。验收限于布局、恢复完整性和数据一致性，没有追加运动或性能测试。
