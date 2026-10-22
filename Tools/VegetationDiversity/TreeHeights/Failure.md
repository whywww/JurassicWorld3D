# 树高调整停止并恢复

System.InvalidOperationException: No safe height: Sample/Assets/NatureStarterKit2/Nature/tree01.prefab at (-17.64, -0.02, 31.08), original=11.360m, allowed=5.00..10.00m.
  at JurassicWorld.Editor.TreeHeightAdjustment.ApplyCurrent (System.Boolean clearance) [0x00389] in E:\Unity\JurassicWorld\Assets\Editor\TreeHeightAdjustment.cs:297 
  at JurassicWorld.Editor.TreeHeightAdjustment.ApplyAndVerify () [0x0011f] in E:\Unity\JurassicWorld\Assets\Editor\TreeHeightAdjustment.cs:131 

整批尺寸已恢复；没有移树或放宽碰撞规则。用户授权的近乎重合树撤下独立保留，详情与恢复记录见OverlapCleanup.md。
